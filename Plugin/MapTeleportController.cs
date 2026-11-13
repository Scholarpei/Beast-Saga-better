using BeastSagaBetter.Core;
using Cinemachine;
using System.Runtime.InteropServices;
using Il2CppInterop.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace BeastSagaBetter;

internal static class MapTeleportController
{
    private const float DefaultRadius = 0.45f;
    private const float CollisionCheckDistance = 0.1f;
    private static Camera? activeCamera;
    private static Camera? capturedCamera;
    private static Vector3 originalCameraPosition;
    private static Vector3 dragStartPosition;
    private static int dragButton = -1;
    private static bool cameraSaved;
    private static CinemachineBrain? cameraBrain;
    private static bool cameraBrainEnabled;
    private static MapTeleportPoint playerStart;
    private static Vector3 desiredCameraPosition;
    private static Vector3 lastDragDelta;
    private static GameObject? previewCameraObject;
    private static int originalCullingMask;
    private static bool originalCullingMaskSaved;
    private static MapTeleportPoint lastStreamingPoint;
    private static VisibilityManager? visibilityManager;
    private static Camera? visibilityManagerOriginalCamera;
    private static int visibilityCameraOffset;
    private static int visibilityUpdateOffset;
    private static int visibilityCheckOffset;
    private static LJG.Framework.CameraManager? gameCameraManager;
    private static Camera? gameCameraManagerOriginalCamera;

    internal static bool ReadingOwnInput { get; private set; }

    public static MapTeleportEvaluation Candidate { get; private set; }

    internal static object? Diagnostics()
    {
        if (!MapTeleportMode.Active) return null;
        var player = SaveManager._Instance?._Player;
        var camera = activeCamera ?? Camera.main;
        var map = MapManager._Instance;
        var helper = LogicHelper._Instance;
        var point = Candidate.Point;
        var world = new Vector3(point.X, point.Y, map?.MapPlaneZ ?? 0f);
        var overlaps = Physics2D.OverlapPointAll(new Vector2(point.X, point.Y));
        return new
        {
            candidate = Candidate,
            world = world.ToString(),
            helperObstacle = helper?.CheckObstacle(world, world, CollisionCheckDistance),
            overlaps = overlaps.Select(collider => new
            {
                name = collider.name,
                layer = collider.gameObject.layer,
                layerName = LayerMask.LayerToName(collider.gameObject.layer),
                tag = collider.tag,
                isTrigger = collider.isTrigger,
                bounds = collider.bounds.ToString()
            }).Take(20).ToArray(),
            player = player == null ? null : new
            {
                position = player.transform.position.ToString(),
                components = player.GetComponents<Component>().Select(component => component.GetType().Name).ToArray()
            },
            camera = camera == null ? null : new
            {
                position = camera.transform.position.ToString(),
                desired = desiredCameraPosition.ToString(),
                components = camera.GetComponents<Component>().Select(component =>
                    component.GetIl2CppType().FullName + ":" + (component is Behaviour behaviour ? behaviour.enabled : false)).ToArray()
            },
            inputMouse = Input.mousePosition.ToString(),
            edgePan = MapTeleportMode.EdgePan(Input.mousePosition.x, Input.mousePosition.y,
                Screen.width, Screen.height).ToString(),
            dragButton,
            lastDragDelta = lastDragDelta.ToString(),
            visibilityCamera = visibilityManager == null || visibilityCameraOffset == 0
                ? null
                : ReadVisibilityCamera()?.transform.position.ToString(),
            cameraManager = gameCameraManager?.MainCamera == null
                ? null
                : gameCameraManager.MainCamera.transform.position.ToString()
        };
    }

    internal static object Probe(float worldX, float worldY)
    {
        var map = MapManager._Instance;
        var player = SaveManager._Instance?._Player;
        var world = new Vector3(worldX, worldY, map?.MapPlaneZ ?? 0f);
        var radius = DefaultRadius;
        var playerCollider = player != null ? player.GetComponent<CircleCollider2D>() : null;
        if (playerCollider != null && playerCollider.radius > 0f && float.IsFinite(playerCollider.radius))
            radius = MathF.Max(playerCollider.radius * MathF.Abs(MathF.Max(player!.transform.lossyScale.x,
                player.transform.lossyScale.y)), 0.15f);
        object Describe(Collider2D collider) => new
        {
            name = collider.name,
            layer = collider.gameObject.layer,
            layerName = LayerMask.LayerToName(collider.gameObject.layer),
            tag = collider.tag,
            isTrigger = collider.isTrigger,
            enabled = collider.enabled,
            active = collider.gameObject.activeInHierarchy,
            bounds = collider.bounds.ToString()
        };
        return new
        {
            world = world.ToString(),
            radius,
            savePlayer = SaveManager._Instance?._Player?.transform.position.ToString(),
            playerDataPlayer = PlayerDataManager._Instance?._Player?.transform.position.ToString(),
            playerLogic = PlayerLogic._Instance?.transform.position.ToString(),
            pointHasSolid = Physics2D.OverlapPointAll(new Vector2(worldX, worldY)).Any(collider => !collider.isTrigger),
            pointHits = Physics2D.OverlapPointAll(new Vector2(worldX, worldY)).Select(Describe).ToArray(),
            circleHits = Physics2D.OverlapCircleAll(new Vector2(worldX, worldY), radius).Select(Describe).ToArray(),
            playerComponents = player?.GetComponents<Component>().Select(component => component.GetType().Name).ToArray(),
            colliderScene = UnityEngine.Object.FindObjectsOfType<Collider2D>()
                .GroupBy(collider => LayerMask.LayerToName(collider.gameObject.layer) + "/" + collider.GetType().Name + "/" + (collider.isTrigger ? "trigger" : "solid"))
                .Select(group => new { key = group.Key, count = group.Count() })
                .ToArray(),
            camera = Camera.main == null ? null : new
            {
                position = Camera.main.transform.position.ToString(),
                components = Camera.main.GetComponents<Component>().Select(component => component.GetType().Name).ToArray()
            }
        };
    }

    public static void Begin()
    {
        if (MapTeleportMode.Active) return;
        if (CheatEditor.EditBlockReason() is { } reason)
        {
            Plugin.Tip(reason);
            return;
        }
        var camera = Camera.main;
        var map = MapManager._Instance;
        if (camera == null || map == null || string.IsNullOrWhiteSpace(map.GetMapName()))
        {
            Plugin.Tip("地图或相机尚未准备好");
            return;
        }
        activeCamera = camera;
        capturedCamera = camera;
        originalCameraPosition = camera.transform.position;
        desiredCameraPosition = originalCameraPosition;
        cameraSaved = true;
        originalCullingMask = camera.cullingMask;
        originalCullingMaskSaved = true;
        previewCameraObject = CreatePreviewCamera(camera, desiredCameraPosition);
        camera.cullingMask = 0;
        activeCamera = previewCameraObject.GetComponent<Camera>();
        BindVisibilityToPreview(activeCamera);
        UpdateStreaming(force: true);
        playerStart = SaveManager._Instance?._Player != null
            ? new MapTeleportPoint(SaveManager._Instance._Player.transform.position.x,
                SaveManager._Instance._Player.transform.position.y)
            : new MapTeleportPoint(originalCameraPosition.x, originalCameraPosition.y);
        cameraBrain = camera.GetComponent<CinemachineBrain>();
        if (cameraBrain != null)
        {
            cameraBrainEnabled = cameraBrain.enabled;
            cameraBrain.enabled = false;
        }
        dragButton = -1;
        Candidate = default;
        MapTeleportMode.Open();
        CommandPanelState.Close();
        Plugin.PanelNoteEditing = false;
        Plugin.Tip("地图点击传送：左键短点传送，按住鼠标拖动查看，Esc退出");
    }

    public static void Update()
    {
        if (!MapTeleportMode.Active) return;
        ReadingOwnInput = true;
        try
        {
            UpdateWithOwnInput();
        }
        finally
        {
            ReadingOwnInput = false;
        }
    }

    public static void LateUpdate()
    {
        if (MapTeleportMode.Active && activeCamera != null)
        {
            activeCamera.transform.position = desiredCameraPosition;
            UpdateStreaming(force: false);
        }
    }

    private static void UpdateWithOwnInput()
    {
        if (!Application.isFocused)
        {
            Close("窗口失焦，地图传送已退出");
            return;
        }
        var blockReason = CheatEditor.EditBlockReason();
        if (blockReason is not null)
        {
            Close(blockReason);
            return;
        }
        if (activeCamera == null) return;
        if (PanelKeyGate.ReadKeyDown(Plugin.ClosePanelKey.Value) ||
            PanelKeyGate.ReadKeyDown(Plugin.PanelKey.Value))
        {
            PanelKeyGate.Consume();
            Close("已退出地图点击传送");
            return;
        }

        var mouse = Input.mousePosition;
        var elapsed = Time.deltaTime;
        var edgePan = MapTeleportMode.EdgePan(mouse.x, mouse.y, Screen.width, Screen.height);
        desiredCameraPosition += new Vector3(edgePan.X * elapsed, edgePan.Y * elapsed, 0f);
        activeCamera.transform.position = desiredCameraPosition;

        for (var button = 0; button <= 2; button++)
        {
            if (Input.GetMouseButtonDown(button))
            {
                dragButton = button;
                dragStartPosition = mouse;
                if (button == 0) MapTeleportMode.BeginPointer(mouse.x, mouse.y);
            }
        }

        MapTeleportMode.ObservePointer(mouse.x, mouse.y);
        if (dragButton >= 0 && Input.GetMouseButton(dragButton))
        {
            var delta = mouse - dragStartPosition;
            lastDragDelta = delta;
            if (MapTeleportDrag.ShouldMove(delta.x, delta.y))
            {
                var pixelsPerWorldUnit = Screen.height / MathF.Max(activeCamera.orthographicSize * 2f, 1e-6f);
                desiredCameraPosition -= new Vector3(
                    delta.x / pixelsPerWorldUnit, delta.y / pixelsPerWorldUnit, 0f);
                activeCamera.transform.position = desiredCameraPosition;
                dragStartPosition = mouse;
            }
        }
        if (dragButton >= 0 && Input.GetMouseButtonUp(dragButton)) dragButton = -1;

        Candidate = Evaluate(mouse);
        if (Input.GetMouseButtonUp(0) && MapTeleportMode.IsClickReleased(mouse.x, mouse.y))
        {
            if (!Candidate.IsValid)
            {
                Plugin.Tip("该位置不可传送");
                return;
            }
            Teleport(Candidate.Point);
        }
    }

    public static void Draw()
    {
        if (!MapTeleportMode.Active) return;
        var inputMouse = Input.mousePosition;
        var mousePoint = MapTeleportScreen.ToGui(new MapTeleportPoint(inputMouse.x, inputMouse.y), Screen.height);
        var mouse = new Vector2(mousePoint.X, mousePoint.Y);
        var candidate = Candidate;
        var color = candidate.IsValid ? Color.green : Color.red;
        var oldColor = GUI.color;
        GUI.color = new Color(color.r, color.g, color.b, 0.78f);
        var center = new Vector2(mouse.x, mouse.y);
        GUI.DrawTexture(new Rect(center.x - 13, center.y - 13, 26, 26), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(center.x - 26, center.y - 1, 52, 2), Texture2D.whiteTexture);
        GUI.DrawTexture(new Rect(center.x - 1, center.y - 26, 2, 52), Texture2D.whiteTexture);
        GUI.color = oldColor;
        var map = MapManager._Instance;
        var label = $"{map?.GetMapNameInfo()} · ({candidate.Point.X:0.00}, {candidate.Point.Y:0.00}) · " +
                    (candidate.IsValid ? "可传送" : "不可传送");
        var rect = new Rect(Math.Min(mouse.x + 22, Screen.width - 300), Math.Min(mouse.y + 22, Screen.height - 76), 280, 58);
        GUI.Box(rect, "");
        GUI.Label(rect, label + "\n左键短点传送 · 按住鼠标拖动 · 靠近屏幕边缘自动移动 · Esc退出");
    }

    public static void Close(string message)
    {
        RestoreOriginalCamera();
        RestoreCameraBrain();
        activeCamera = null;
        capturedCamera = null;
        cameraSaved = false;
        dragButton = -1;
        lastDragDelta = Vector3.zero;
        Candidate = default;
        MapTeleportMode.Close();
        if (!string.IsNullOrWhiteSpace(message)) Plugin.Tip(message);
    }

    public static void Destroy()
    {
        Close(string.Empty);
    }

    private static MapTeleportEvaluation Evaluate(Vector3 screenPosition)
    {
        var camera = activeCamera ?? Camera.main;
        var map = MapManager._Instance;
        if (camera == null || map == null) return default;
        var ray = camera.ScreenPointToRay(screenPosition);
        var denominator = Vector3.Dot(Vector3.forward, ray.direction);
        if (MathF.Abs(denominator) < 1e-6f) return default;
        var distance = (map.MapPlaneZ - ray.origin.z) / denominator;
        var point = ray.origin + ray.direction * distance;
        if (!float.IsFinite(point.x) || !float.IsFinite(point.y)) return default;
        var player = SaveManager._Instance?._Player;
        var radius = DefaultRadius;
        var collider = player != null ? player.GetComponent<CircleCollider2D>() : null;
        if (collider != null && collider.radius > 0f && float.IsFinite(collider.radius))
            radius = MathF.Max(collider.radius * MathF.Abs(MathF.Max(player!.transform.lossyScale.x, player.transform.lossyScale.y)), 0.15f);
        bool AreaSafe(float x, float y) => Physics2D.OverlapCircleAll(new Vector2(x, y), radius)
            .All(collider => !MapTeleportCollisionPolicy.Blocks(new MapTeleportColliderInfo(
                collider.isTrigger, LayerMask.LayerToName(collider.gameObject.layer), collider.tag)));
        bool NudgedSafe(float x, float y) => AreaSafe(x, y);
        return MapTeleportCandidate.Evaluate(point.x, point.y, AreaSafe, NudgedSafe);
    }

    private static void Teleport(MapTeleportPoint point)
    {
        var map = MapManager._Instance;
        var mapName = map?.GetMapName();
        if (map == null || string.IsNullOrWhiteSpace(mapName))
        {
            Close("当前地图不可传送");
            return;
        }
        var target = new Vector3(point.X, point.Y, map.MapPlaneZ);
        try
        {
            var player = SaveManager._Instance?._Player;
            if (player == null)
            {
                Close("玩家尚未准备好，地图传送已退出");
                return;
            }
            Vector3? cameraEnd = null;
            if (capturedCamera != null && cameraSaved)
            {
                var cameraStart = new MapTeleportPoint(originalCameraPosition.x,
                    originalCameraPosition.y);
                var end = MapTeleportCamera.EndPosition(playerStart, cameraStart, point);
                cameraEnd = new Vector3(end.X, end.Y, originalCameraPosition.z);
            }
            Plugin.Logger.LogInfo(
                $"地图点击传送：map={mapName} info={map.GetMapNameInfo()} from={player.transform.position} target={target}");
            RestoreOriginalCamera(syncMap: false);
            if (cameraEnd.HasValue && capturedCamera != null)
                capturedCamera.transform.position = cameraEnd.Value;
            activeCamera = null;
            capturedCamera = null;
            cameraSaved = false;
            player.transform.position = target;
            map.UpdateMap(target);
            map.LoadCheck(new Vector2(target.x, target.y));
            MapTeleportMode.Close();
            RestoreCameraBrain();
            Plugin.Tip("已传送到标记位置");
        }
        catch (Exception error)
        {
            Plugin.Logger.LogError("地图点击传送失败：" + error);
            Close("地图传送失败，已退出选择模式");
        }
    }

    internal static void TeleportForTest(float x, float y) => Teleport(new MapTeleportPoint(x, y));

    internal static void MoveCameraForTest(float x, float y)
    {
        if (!MapTeleportMode.Active) return;
        desiredCameraPosition = new Vector3(x, y, desiredCameraPosition.z);
        if (activeCamera != null) activeCamera.transform.position = desiredCameraPosition;
        UpdateStreaming(force: true);
    }

    internal static void CloseForTest() => Close("已退出地图点击传送");

    private static void RestoreCameraBrain()
    {
        if (cameraBrain != null) cameraBrain.enabled = cameraBrainEnabled;
        cameraBrain = null;
    }

    private static GameObject CreatePreviewCamera(Camera source, Vector3 position)
    {
        var clone = new GameObject("BeastSagaBetterMapPreviewCamera");
        clone.tag = "Untagged";
        clone.SetActive(false);
        var camera = clone.AddComponent<Camera>();
        CopyCameraSettings(source, camera);
        CopyCameraData(source, clone);
        clone.transform.SetPositionAndRotation(position, source.transform.rotation);
        clone.transform.localScale = source.transform.localScale;
        clone.SetActive(true);
        return clone;
    }

    private static void CopyCameraSettings(Camera source, Camera target)
    {
        target.clearFlags = source.clearFlags;
        target.backgroundColor = source.backgroundColor;
        target.cullingMask = originalCullingMask;
        target.depth = source.depth;
        target.orthographic = source.orthographic;
        target.orthographicSize = source.orthographicSize;
        target.fieldOfView = source.fieldOfView;
        target.nearClipPlane = source.nearClipPlane;
        target.farClipPlane = source.farClipPlane;
        target.rect = source.rect;
        target.allowHDR = source.allowHDR;
        target.allowMSAA = source.allowMSAA;
        target.allowDynamicResolution = source.allowDynamicResolution;
        target.useOcclusionCulling = source.useOcclusionCulling;
        target.usePhysicalProperties = source.usePhysicalProperties;
        target.sensorSize = source.sensorSize;
        target.lensShift = source.lensShift;
        target.gateFit = source.gateFit;
        target.depthTextureMode = source.depthTextureMode;
        target.targetDisplay = source.targetDisplay;
        target.targetTexture = source.targetTexture;
        target.enabled = true;
    }

    private static void CopyCameraData(Camera source, GameObject clone)
    {
        var sourceData = source.GetComponent<UniversalAdditionalCameraData>();
        if (sourceData == null) return;
        var targetData = clone.AddComponent<UniversalAdditionalCameraData>();
        try
        {
            JsonUtility.FromJsonOverwrite(JsonUtility.ToJson(sourceData), targetData);
        }
        catch (Exception error)
        {
            Plugin.Logger.LogWarning("地图传送预览相机未复制 URP 相机数据，将使用默认值：" + error.Message);
        }
    }

    private static void BindVisibilityToPreview(Camera preview)
    {
        visibilityManager = VisibilityManager._Instance;
        if (visibilityManager == null)
            visibilityManager = UnityEngine.Object.FindObjectOfType<VisibilityManager>();
        visibilityCameraOffset = FieldOffset(typeof(VisibilityManager), "mainCamera");
        visibilityUpdateOffset = FieldOffset(typeof(VisibilityManager), "isUpdate");
        visibilityCheckOffset = FieldOffset(typeof(VisibilityManager), "checkUpate");
        if (visibilityManager != null && visibilityCameraOffset != 0)
        {
            var managerPointer = IL2CPP.Il2CppObjectBaseToPtr(visibilityManager);
            visibilityManagerOriginalCamera = ReadVisibilityCamera();
            Marshal.WriteIntPtr(managerPointer + visibilityCameraOffset,
                IL2CPP.Il2CppObjectBaseToPtr(preview));
            if (visibilityUpdateOffset != 0)
                Marshal.WriteByte(managerPointer + visibilityUpdateOffset, 1);
            if (visibilityCheckOffset != 0)
                Marshal.WriteByte(managerPointer + visibilityCheckOffset, 1);
            Plugin.Logger.LogInfo("地图点击传送：可见性管理器已绑定预览相机");
        }
        else
        {
            Plugin.Logger.LogWarning("地图点击传送：未找到 VisibilityManager，远处建筑/NPC 可能不会加载");
        }

        gameCameraManager = LJG.Framework.Main.m_Camera;
        if (gameCameraManager == null) return;
        gameCameraManagerOriginalCamera = gameCameraManager.MainCamera;
        gameCameraManager.MainCamera = preview;
    }

    private static void RestoreVisibilityCamera()
    {
        if (visibilityManager != null && visibilityCameraOffset != 0)
        {
            var managerPointer = IL2CPP.Il2CppObjectBaseToPtr(visibilityManager);
            var restoreCamera = visibilityManagerOriginalCamera ?? capturedCamera;
            Marshal.WriteIntPtr(managerPointer + visibilityCameraOffset,
                restoreCamera == null ? IntPtr.Zero : IL2CPP.Il2CppObjectBaseToPtr(restoreCamera));
            if (visibilityCheckOffset != 0)
                Marshal.WriteByte(managerPointer + visibilityCheckOffset, 1);
        }
        visibilityManager = null;
        visibilityManagerOriginalCamera = null;
        visibilityCameraOffset = 0;
        visibilityUpdateOffset = 0;
        visibilityCheckOffset = 0;

        if (gameCameraManager != null)
            gameCameraManager.MainCamera = gameCameraManagerOriginalCamera;
        gameCameraManager = null;
        gameCameraManagerOriginalCamera = null;
    }

    private static Camera? ReadVisibilityCamera()
    {
        if (visibilityManager == null || visibilityCameraOffset == 0) return null;
        var managerPointer = IL2CPP.Il2CppObjectBaseToPtr(visibilityManager);
        var cameraPointer = Marshal.ReadIntPtr(managerPointer + visibilityCameraOffset);
        return cameraPointer == IntPtr.Zero ? null : new Camera(cameraPointer);
    }

    private static int FieldOffset(Type type, string name)
    {
        var field = type.GetField("NativeFieldInfoPtr_" + name,
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        if (field == null) return 0;
        var pointer = (IntPtr)field.GetValue(null)!;
        return pointer == IntPtr.Zero ? 0 : checked((int)IL2CPP.il2cpp_field_get_offset(pointer));
    }

    private static void RestoreOriginalCamera(bool syncMap = true)
    {
        RestoreVisibilityCamera();
        if (capturedCamera != null && originalCullingMaskSaved)
            capturedCamera.cullingMask = originalCullingMask;
        originalCullingMaskSaved = false;
        if (previewCameraObject != null)
        {
            UnityEngine.Object.Destroy(previewCameraObject);
            previewCameraObject = null;
        }
        if (capturedCamera != null && cameraSaved)
            capturedCamera.transform.position = originalCameraPosition;
        if (!syncMap) return;
        var player = SaveManager._Instance?._Player;
        var map = MapManager._Instance;
        if (player != null && map != null)
        {
            map.UpdateMap(player.transform.position);
            map.LoadCheck(new Vector2(player.transform.position.x, player.transform.position.y));
        }
    }

    private static void UpdateStreaming(bool force)
    {
        var map = MapManager._Instance;
        if (map == null) return;
        var current = new MapTeleportPoint(desiredCameraPosition.x, desiredCameraPosition.y);
        if (!force && !MapTeleportStreaming.ShouldUpdate(lastStreamingPoint, current)) return;
        lastStreamingPoint = current;
        map.TickStreamingDistanceOnly(new Vector3(current.X, current.Y, map.MapPlaneZ));
        visibilityManager?.ShowPlayerPositionSame(current.X, current.Y);
    }

}

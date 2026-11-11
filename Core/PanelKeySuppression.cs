namespace SafeDialogueTools.Core;

public static class PanelKeySuppression
{
    public static bool Blocks(bool isPanelKey, bool panelOpen, int consumedFrame, int currentFrame) =>
        isPanelKey && (panelOpen || consumedFrame == currentFrame);
}

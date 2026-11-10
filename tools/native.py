import bisect
import json
import struct
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parent / 'vendor'))
import capstone
import pefile

ROOT = Path(__file__).resolve().parents[2]
database = (ROOT / 'BepInEx/interop/MethodAddressToToken.db').read_bytes()
magic, version, assembly_count, method_count, offset = struct.unpack_from('<5i', database)
cursor = 20
assemblies = []
for assembly_index in range(assembly_count):
    size = 0
    shift = 0
    while True:
        value = database[cursor]
        cursor += 1
        size |= (value & 127) << shift
        if value < 128:
            break
        shift += 7
    assemblies.append(database[cursor:cursor + size].decode().split(',')[0])
    cursor += size
addresses = struct.unpack_from(f'<{method_count}q', database, offset)
values = [struct.unpack_from('<2i', database, offset + method_count * 8 + index * 8)
          for index in range(method_count)]
names = json.loads((Path(__file__).parent / 'methods.json').read_text(encoding='utf-8-sig'))

def method_name(index):
    token, assembly_index = values[index]
    assembly = assemblies[assembly_index]
    return names.get(assembly, {}).get(str(token), f'{assembly}:0x{token:x}')

def resolve(address):
    index = bisect.bisect_left(addresses, address)
    return method_name(index) if index < len(addresses) and addresses[index] == address else ''

image = pefile.PE(str(ROOT / 'GameAssembly.dll'), fast_load=True)
metadata = (ROOT / 'BeastSaga_Data/il2cpp_data/Metadata/global-metadata.dat').read_bytes()
literal_offset, literal_size, literal_data_offset = struct.unpack_from('<3I', metadata, 8)
base = image.OPTIONAL_HEADER.ImageBase
decoder = capstone.Cs(capstone.CS_ARCH_X86, capstone.CS_MODE_64)
decoder.detail = True
patterns = sys.argv[1:]
for index, address in enumerate(addresses):
    name = method_name(index)
    if not any(pattern in name for pattern in patterns):
        continue
    print(f'\n=== 0x{address:x} {name} ===')
    following = bisect.bisect_right(addresses, address)
    next_address = addresses[following] if following < len(addresses) else address + 4096
    size = min(next_address - address, 16000)
    raw = image.get_data(address, size)
    for instruction in decoder.disasm(raw, base + address):
        extra = ''
        if instruction.mnemonic in ['call', 'jmp'] and instruction.operands:
            operand = instruction.operands[0]
            if operand.type == capstone.x86.X86_OP_IMM:
                extra = resolve(operand.imm - base)
        for operand in instruction.operands:
            if operand.type == capstone.x86.X86_OP_MEM and operand.mem.base == capstone.x86.X86_REG_RIP:
                target = instruction.address + instruction.size + operand.mem.disp - base
                try:
                    token = struct.unpack('<Q', image.get_data(target, 8))[0]
                    if token >> 29 == 5:
                        literal_index = (token & 0x1fffffff) >> 1
                        length, position = struct.unpack_from('<2I', metadata, literal_offset + literal_index * 8)
                        text = metadata[literal_data_offset + position:literal_data_offset + position + length].decode('utf-8')
                        extra += ' STRING=' + repr(text)
                except (ValueError, struct.error, UnicodeDecodeError):
                    pass
        print(f'{instruction.address-base:08x}: {instruction.mnemonic:8} {instruction.op_str:42} {extra}')

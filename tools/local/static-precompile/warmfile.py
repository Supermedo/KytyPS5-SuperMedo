"""warmlib.py -- strict decoder + validator for KytyShaderWarmup2 caches.

Mirrors, field for field and word for word:
  * src/local/shader-warmup-cache.h   (Visit, VisitPipeline, Cache::Load/ReadRecords/ValidPipeline)
  * src/graphics/shader/shader.cpp    (BuildStageStaticKey, three overloads)
  * src/graphics/shader/shaderBindings.h (ShaderBufferResource accessors used by the VS key)

Everything is read-only.  Nothing here touches the repo or the game directory except reading.
"""
from __future__ import annotations

import hashlib
import re
import struct
import sys
from pathlib import Path

import numpy as np

# Packed as kyty_seeds.exe (PyInstaller): the headers read below are bundled at the same relative paths.
REPO = Path(sys._MEIPASS) if getattr(sys, 'frozen', False) else Path(__file__).resolve().parents[3]
CACHE_ROOT = REPO / '_PipelineCache' / 'warmup-v2'
OUT = REPO / '_Build' / 're' / 'agent-warm'

NO_SHADER = 0xFFFFFFFF
ST_UNKNOWN, ST_VERTEX, ST_PIXEL, ST_FETCH, ST_COMPUTE, ST_MESH = range(6)
STAGE_NAME = {0: 'Unknown', 1: 'Vertex', 2: 'Pixel', 3: 'Fetch', 4: 'Compute', 5: 'Mesh'}
RES_MAX = 32                      # ShaderVertexInputInfo::RES_MAX
RENDER_COLOR_ATTACHMENTS_MAX = 8
MAX_BUFFERS = 32                  # ShaderInfo::MaxBuffers
MAX_IMAGES = 64                   # ShaderInfo::MaxImages
MAX_RECORDS = 16384
MAX_PIPELINES = 65536
M32 = 0xFFFFFFFF
M64 = 0xFFFFFFFFFFFFFFFF

# Defaults of ResourceSpecialization::Buffer / ::Image (ResourceMaterialization.h)
DEF_SWIZZLE = 4 | (5 << 3) | (6 << 6) | (7 << 9)     # DstSel(4,5,6,7) == 0xFAC
DEF_BUFFER = (0, 0, DEF_SWIZZLE, 0)
DEF_IMAGE = (0, 0, 1, 0, 0xFAC, M32, 0, 0, 0, 0)
BUFFER_FIELDS = ('packed_stride', 'descriptor_format', 'descriptor_swizzle', 'byte_base_offset')
IMAGE_FIELDS = ('numeric_class', 'dimension', 'mip_count', 'conversion_format', 'shader_swizzle',
                'indirect_root', 'indirect_mapping_offset', 'indirect_search_iterations', 'cube',
                'fmask')


# ---------------------------------------------------------------------------------------------
# XXH3-64 (seed 0, default secret) -- only the >240 byte path is needed (cache bodies are MBs).
# Secret + algorithm taken from 3rdparty/xxHash/xxhash.h in the repo.
# ---------------------------------------------------------------------------------------------
_SECRET = bytes([
    0xb8, 0xfe, 0x6c, 0x39, 0x23, 0xa4, 0x4b, 0xbe, 0x7c, 0x01, 0x81, 0x2c, 0xf7, 0x21, 0xad, 0x1c,
    0xde, 0xd4, 0x6d, 0xe9, 0x83, 0x90, 0x97, 0xdb, 0x72, 0x40, 0xa4, 0xa4, 0xb7, 0xb3, 0x67, 0x1f,
    0xcb, 0x79, 0xe6, 0x4e, 0xcc, 0xc0, 0xe5, 0x78, 0x82, 0x5a, 0xd0, 0x7d, 0xcc, 0xff, 0x72, 0x21,
    0xb8, 0x08, 0x46, 0x74, 0xf7, 0x43, 0x24, 0x8e, 0xe0, 0x35, 0x90, 0xe6, 0x81, 0x3a, 0x26, 0x4c,
    0x3c, 0x28, 0x52, 0xbb, 0x91, 0xc3, 0x00, 0xcb, 0x88, 0xd0, 0x65, 0x8b, 0x1b, 0x53, 0x2e, 0xa3,
    0x71, 0x64, 0x48, 0x97, 0xa2, 0x0d, 0xf9, 0x4e, 0x38, 0x19, 0xef, 0x46, 0xa9, 0xde, 0xac, 0xd8,
    0xa8, 0xfa, 0x76, 0x3f, 0xe3, 0x9c, 0x34, 0x3f, 0xf9, 0xdc, 0xbb, 0xc7, 0xc7, 0x0b, 0x4f, 0x1d,
    0x8a, 0x51, 0xe0, 0x4b, 0xcd, 0xb4, 0x59, 0x31, 0xc8, 0x9f, 0x7e, 0xc9, 0xd9, 0x78, 0x73, 0x64,
    0xea, 0xc5, 0xac, 0x83, 0x34, 0xd3, 0xeb, 0xc3, 0xc5, 0x81, 0xa0, 0xff, 0xfa, 0x13, 0x63, 0xeb,
    0x17, 0x0d, 0xdd, 0x51, 0xb7, 0xf0, 0xda, 0x49, 0xd3, 0x16, 0x55, 0x26, 0x29, 0xd4, 0x68, 0x9e,
    0x2b, 0x16, 0xbe, 0x58, 0x7d, 0x47, 0xa1, 0xfc, 0x8f, 0xf8, 0xb8, 0xd1, 0x7a, 0xd0, 0x31, 0xce,
    0x45, 0xcb, 0x3a, 0x8f, 0x95, 0x16, 0x04, 0x28, 0xaf, 0xd7, 0xfb, 0xca, 0xbb, 0x4b, 0x40, 0x7e,
])
_P32_1, _P32_2, _P32_3 = 0x9E3779B1, 0x85EBCA77, 0xC2B2AE3D
_P64_1, _P64_2, _P64_3 = 0x9E3779B185EBCA87, 0xC2B2AE3D27D4EB4F, 0x165667B19E3779F9
_P64_4, _P64_5 = 0x85EBCA77C2B2AE63, 0x27D4EB2F165667C5
_PRIME_MX1 = 0x165667919E3779F9


def _mix16(data: bytes, off: int, sec_off: int) -> int:
    lo, hi = struct.unpack_from('<QQ', data, off)
    slo, shi = struct.unpack_from('<QQ', _SECRET, sec_off)
    prod = (lo ^ slo) * (hi ^ shi)
    return (prod & M64) ^ (prod >> 64)


def _avalanche(h: int) -> int:
    h ^= h >> 37
    h = (h * _PRIME_MX1) & M64
    h ^= h >> 32
    return h


def _xxh3_17_to_128(data: bytes) -> int:
    n = len(data)
    acc = (n * _P64_1) & M64
    if n > 32:
        if n > 64:
            if n > 96:
                acc += _mix16(data, 48, 96) + _mix16(data, n - 64, 112)
            acc += _mix16(data, 32, 64) + _mix16(data, n - 48, 80)
        acc += _mix16(data, 16, 32) + _mix16(data, n - 32, 48)
    acc += _mix16(data, 0, 0) + _mix16(data, n - 16, 16)
    return _avalanche(acc & M64)


def _xxh3_129_to_240(data: bytes) -> int:
    n = len(data)
    acc = (n * _P64_1) & M64
    rounds = n // 16
    for i in range(8):
        acc = (acc + _mix16(data, 16 * i, 16 * i)) & M64
    acc_end = _mix16(data, n - 16, 136 - 17)
    acc = _avalanche(acc)
    for i in range(8, rounds):
        acc_end = (acc_end + _mix16(data, 16 * i, 16 * (i - 8) + 3)) & M64
    return _avalanche((acc + acc_end) & M64)


def xxh3_64(data: bytes) -> int:
    """XXH3-64, seed 0, default secret.  Implemented for len >= 17 (all shader blobs and cache bodies)."""
    n = len(data)
    if n <= 16:
        raise NotImplementedError('XXH3 short-input paths (<= 16 bytes) are not implemented')
    if n <= 128:
        return _xxh3_17_to_128(data)
    if n <= 240:
        return _xxh3_129_to_240(data)
    acc = [_P32_3, _P64_1, _P64_2, _P64_3, _P64_4, _P32_2, _P64_5, _P32_1]
    stripes_per_block = (192 - 64) // 8
    block_len = 64 * stripes_per_block
    nb_blocks = (n - 1) // block_len
    keys = [struct.unpack_from('<8Q', _SECRET, s * 8) for s in range(stripes_per_block)]
    scr = struct.unpack_from('<8Q', _SECRET, 192 - 64)
    unpack8 = struct.Struct('<8Q').unpack_from

    def stripe(off, key):
        v = unpack8(data, off)
        for i in range(8):
            dv = v[i]
            dk = dv ^ key[i]
            j = i ^ 1
            acc[j] = (acc[j] + dv) & M64
            acc[i] = (acc[i] + (dk & M32) * (dk >> 32)) & M64

    for b in range(nb_blocks):
        base = b * block_len
        for s in range(stripes_per_block):
            stripe(base + s * 64, keys[s])
        for i in range(8):
            a = acc[i]
            a ^= a >> 47
            a ^= scr[i]
            acc[i] = (a * _P32_1) & M64
    base = nb_blocks * block_len
    for s in range(((n - 1) - block_len * nb_blocks) // 64):
        stripe(base + s * 64, keys[s])
    stripe(n - 64, struct.unpack_from('<8Q', _SECRET, 192 - 64 - 7))
    res = (n * _P64_1) & M64
    for i in range(4):
        lo = acc[2 * i] ^ struct.unpack_from('<Q', _SECRET, 11 + 16 * i)[0]
        hi = acc[2 * i + 1] ^ struct.unpack_from('<Q', _SECRET, 11 + 16 * i + 8)[0]
        prod = lo * hi
        res = (res + ((prod & M64) ^ (prod >> 64))) & M64
    res ^= res >> 37
    res = (res * _PRIME_MX1) & M64
    res ^= res >> 32
    return res


# ---------------------------------------------------------------------------------------------
# Enum name tables (parsed from headers in the repo / 3rdparty; read-only)
# ---------------------------------------------------------------------------------------------
def _parse_enum(path: Path, start_regex: str, item_regex: str, strip: str = '') -> dict[int, str]:
    text = path.read_text(encoding='utf-8', errors='replace')
    m = re.search(start_regex, text)
    if not m:
        return {}
    end = text.index('}', m.end())
    out: dict[int, str] = {}
    for it in re.finditer(item_regex, text[m.end():end]):
        name, val = it.group(1), int(it.group(2))
        if val not in out:
            out[val] = name[len(strip):] if strip and name.startswith(strip) else name
    return out


VK_CORE = REPO / '3rdparty' / 'Vulkan-Headers' / 'include' / 'vulkan' / 'vulkan_core.h'
GPU_DEFS = REPO / 'src' / 'graphics' / 'guest_gpu' / 'gpu_defs.h'
_num = r'\s*=\s*(-?\d+)\s*,'
VK_FORMAT = _parse_enum(VK_CORE, r'typedef enum VkFormat \{', r'(VK_FORMAT_\w+)' + _num, 'VK_FORMAT_')
VK_TOPOLOGY = _parse_enum(VK_CORE, r'typedef enum VkPrimitiveTopology \{',
                          r'(VK_PRIMITIVE_TOPOLOGY_\w+)' + _num, 'VK_PRIMITIVE_TOPOLOGY_')
VK_STENCIL_OP = _parse_enum(VK_CORE, r'typedef enum VkStencilOp \{', r'(VK_STENCIL_OP_\w+)' + _num,
                            'VK_STENCIL_OP_')
VK_COMPARE_OP = _parse_enum(VK_CORE, r'typedef enum VkCompareOp \{', r'(VK_COMPARE_OP_\w+)' + _num,
                            'VK_COMPARE_OP_')
VK_POLYGON_MODE = _parse_enum(VK_CORE, r'typedef enum VkPolygonMode \{', r'(VK_POLYGON_MODE_\w+)' + _num,
                              'VK_POLYGON_MODE_')
BUFFER_FORMAT = _parse_enum(GPU_DEFS, r'enum class BufferFormat : uint32_t \{', r'k(\w+)\s*=\s*(\d+),')
PRIM_TYPE = _parse_enum(GPU_DEFS, r'enum class PrimitiveType : uint32_t \{', r'k(\w+)\s*=\s*(\d+),')
NUMERIC_CLASS = {0: 'Unsupported', 1: 'Float', 2: 'Uint', 3: 'Sint'}
IMAGE_DIM = {0: 'Unknown', 1: '1D', 2: '1DArray', 3: '2D', 4: '3D', 5: '2DArray', 6: '2DMsaa',
             7: '2DMsaaArray'}
# SPI_SHADER_COL_FORMAT (presumed meaning of target_output_mode; shader.cpp accepts 0,2,4,5,7,9)
COL_FORMAT = {0: 'ZERO', 1: '32_R', 2: '32_GR', 3: '32_AR', 4: 'FP16_ABGR', 5: 'UNORM16_ABGR',
              6: 'SNORM16_ABGR', 7: 'UINT16_ABGR', 8: 'SINT16_ABGR', 9: '32_ABGR'}


def name_of(table: dict[int, str], v: int) -> str:
    return f'{table[v]}({v})' if v in table else f'#{v}'


# ---------------------------------------------------------------------------------------------
# Word reader (mirrors LocalShaderWarmup::Reader; every violation raises DecodeError)
# ---------------------------------------------------------------------------------------------
class DecodeError(Exception):
    pass


class Rd:
    __slots__ = ('w', 'i', 'n')

    def __init__(self, words):
        self.w = words
        self.i = 0
        self.n = len(words)

    def u(self) -> int:
        i = self.i
        if i >= self.n:
            raise DecodeError('eof')
        self.i = i + 1
        return int(self.w[i])

    def s(self) -> int:                      # C++ `int` field
        v = self.u()
        return v - (1 << 32) if v & 0x80000000 else v

    def b(self) -> int:                      # bool: Reader rejects words > 1
        v = self.u()
        if v > 1:
            raise DecodeError('bool word > 1')
        return v

    def u64(self) -> int:
        lo = self.u()
        hi = self.u()
        return lo | (hi << 32)

    def arr(self, count: int) -> list[int]:
        if count > self.n - self.i:
            raise DecodeError('eof in array')
        out = [int(x) for x in self.w[self.i:self.i + count]]
        self.i += count
        return out

    def barr(self, count: int) -> list[int]:
        out = self.arr(count)
        if any(v > 1 for v in out):
            raise DecodeError('bool word > 1 in array')
        return out

    def vec_size(self, bound: int) -> int:   # LocalShaderWarmup::Vector prologue
        size = self.u()
        if size > bound:
            raise DecodeError('vector size beyond bound')
        if size > self.n - self.i:
            raise DecodeError('vector size beyond remaining words')
        return size

    def words(self, bound: int):             # LocalShaderWarmup::Words (numpy view / list slice)
        size = self.vec_size(bound)
        v = self.w[self.i:self.i + size]
        self.i += size
        return v


# ---------------------------------------------------------------------------------------------
# Records
# ---------------------------------------------------------------------------------------------
class Record:
    __slots__ = ('index', 'stage', 'hash', 'udc', 'push', 'code', 'back', 'key', 'info', 'buffers',
                 'images', 'nwords', 'cid', 'code_words', 'raw')

    @property
    def spec(self):
        return (self.buffers, self.images)

    @property
    def stage_name(self):
        return STAGE_NAME[self.stage]

    def entry_key(self):
        """ProgramCache::ProgramKey: (stage, hash, user_data_count, code_size, static_state)."""
        return (self.stage, self.hash, self.udc, self.code_words, self.key)

    def content_id(self) -> bytes:
        return self.cid


def decode_record(w, index: int = -1) -> Record:
    """Visit(Reader&, Record&) + the `entry.cursor == record.size()` check of ReadRecords."""
    r = Rd(w)
    rec = Record()
    rec.index = index
    rec.nwords = len(w)
    rec.stage = r.u()
    rec.hash = r.u64()
    rec.udc = r.u()
    rec.push = r.u()
    code = r.words(256 * 1024)
    back = r.words(256 * 1024)
    key = r.words(1024)
    if len(code) == 0 or rec.udc > 108 or rec.push > 65536:
        raise DecodeError('code empty / udc / push out of range')
    rec.code = bytes(np.asarray(code, dtype='<u4').tobytes())
    rec.back = bytes(np.asarray(back, dtype='<u4').tobytes())
    rec.key = tuple(int(x) for x in key)
    rec.code_words = len(code)
    # the remaining (small) tail as python ints
    tail = [int(x) for x in np.asarray(w[r.i:], dtype=np.uint32)]
    t = Rd(tail)
    st = rec.stage
    info: dict = {}
    if st in (ST_VERTEX, ST_MESH):
        info['resources_num'] = t.s()
        info['fetch_attrib_reg'] = t.s()
        info['fetch_buffer_reg'] = t.s()
        info['scratch_size_dwords'] = t.u()
        info['pa_cl_vs_out_cntl'] = t.u()
        info['fetch_external'] = t.b()
        info['fetch_embedded'] = t.b()
        info['clip_enabled'] = t.b()
        info['clip_scale'] = tuple(t.arr(2))            # raw float bits
        info['clip_offset'] = tuple(t.arr(2))
        info['clip_half_extent'] = tuple(t.arr(2))
        m = {}
        m['threads_num'] = tuple(t.arr(3))
        m['lds_size_dwords'] = t.u()
        m['scratch_size_dwords'] = t.u()
        m['host_subgroup_size'] = t.u()
        m['wave_size'] = t.u()
        m['input_primitive'] = t.u()
        m['primitives_per_group'] = t.u()
        m['vertices_per_group'] = t.u()
        m['max_vertices'] = t.u()
        m['max_primitives'] = t.u()
        m['provoking_vertex'] = t.u()
        info['mesh'] = m
        n = info['resources_num']
        if n < 0 or n > RES_MAX:
            raise DecodeError('resources_num out of range')
        res_fields, res_dst = [], []
        for _ in range(n):
            res_fields.append(tuple(t.arr(4)))
            res_dst.append((t.s(), t.s(), t.s(), t.u()))  # register_start, registers_num, attr_id, fetch_index
        info['res_fields'] = res_fields
        info['res_dst'] = res_dst
        if (st == ST_MESH) != (m['threads_num'][0] != 0):
            raise DecodeError('mesh stage / threads_num[0] mismatch')
    elif st == ST_PIXEL:
        names = ('lod_stats_subgroup', 'input_num', 'ps_system_input_base', 'custom_interpolation_mask',
                 'ps_perspective_center_vgpr', 'scratch_size_dwords', 'ps_pos_x', 'ps_pos_y', 'ps_pos_z',
                 'ps_pos_w', 'ps_front_face', 'ps_ancillary', 'ps_no_perspective', 'ps_pixel_kill_enable',
                 'ps_depth_export_enable', 'ps_sample_mask_export_enable', 'ps_sample_shading',
                 'ps_early_z', 'ps_execute_on_noop')
        bools = {'lod_stats_subgroup', 'ps_pos_x', 'ps_pos_y', 'ps_pos_z', 'ps_pos_w', 'ps_front_face',
                 'ps_ancillary', 'ps_no_perspective', 'ps_pixel_kill_enable', 'ps_depth_export_enable',
                 'ps_sample_mask_export_enable', 'ps_sample_shading', 'ps_early_z', 'ps_execute_on_noop'}
        for nm in names:
            info[nm] = t.b() if nm in bools else t.u()
        info['interpolator_settings'] = tuple(t.arr(32))
        tom = t.arr(8)
        info['target_output_mode'] = tuple(tom)
        info['target_export_mapping'] = tuple(t.arr(8))
        if any(v > 255 for v in tom) or any(v > 255 for v in info['target_export_mapping']):
            raise DecodeError('uint8 field wider than 8 bits')   # not a C++ failure, but never written
        if info['input_num'] > 32:
            raise DecodeError('input_num > 32')
    elif st == ST_COMPUTE:
        info['threads_num'] = tuple(t.arr(3))
        info['lds_size_dwords'] = t.u()
        info['scratch_size_dwords'] = t.u()
        info['host_subgroup_size'] = t.u()
        info['wave_size'] = t.u()
        info['dispatch_threads_num'] = tuple(t.arr(3))
        info['group_id'] = tuple(t.barr(3))
        info['dispatch_thread_dimensions'] = t.b()
        info['thread_ids_num'] = t.s()
        info['workgroup_register'] = t.s()
        info['tg_size_en'] = t.b()
    else:
        raise DecodeError(f'unsupported stage {st}')
    info['_kind'] = 'cs' if st == ST_COMPUTE else ('ps' if st == ST_PIXEL else 'vs')
    rec.info = info
    nb = t.vec_size(MAX_BUFFERS)
    rec.buffers = tuple(tuple(t.u() for _ in range(3)) + (t.b(),) for _ in range(nb))
    ni = t.vec_size(MAX_IMAGES)
    imgs = []
    for _ in range(ni):
        numeric_class = t.u(); dimension = t.u(); mip_count = t.u(); conv = t.u(); swz = t.u()
        root = t.u(); mapoff = t.u(); iters = t.u(); cube = t.b(); fmask = t.b()
        imgs.append((numeric_class, dimension, mip_count, conv, swz, root, mapoff, iters, cube, fmask))
    rec.images = tuple(imgs)
    if t.i != t.n:
        raise DecodeError(f'{t.n - t.i} trailing words in record')
    rec.cid = hashlib.blake2b(np.asarray(w, dtype='<u4').tobytes(), digest_size=16).digest()
    rec.raw = None
    return rec


# ---------------------------------------------------------------------------------------------
# Static key (BuildStageStaticKey) -- rebuilt from the decoded input info, with component names
# ---------------------------------------------------------------------------------------------
def _res_derived(f):
    f0, f1, f2, f3 = f
    return {
        'stride': (f1 >> 16) & 0x3FFF,
        'swizzle_enabled': (f1 >> 31) & 1,
        'dstsel_x': f3 & 7, 'dstsel_y': (f3 >> 3) & 7, 'dstsel_z': (f3 >> 6) & 7, 'dstsel_w': (f3 >> 9) & 7,
        'raw_format': (f3 >> 12) & 0x7F,
        'out_of_bounds': (f3 >> 28) & 3,
        'add_tid': (f3 >> 23) & 1,
        'index_stride': (f3 >> 21) & 3,
        'num_records': f2,
        'base48': (f0 | (f1 << 32)) & 0xFFFFFFFFFFFF,
    }


def key_components(rec: Record) -> list[tuple[str, int, int]]:
    """Ordered [(component, index, u32 value)] exactly as BuildStageStaticKey pushes them.

    index == -1 for scalars.  The concatenation of the values must equal rec.key."""
    i = rec.info
    out: list[tuple[str, int, int]] = []

    def add(name, value, idx=-1):
        out.append((name, idx, int(value) & M32))

    kind = i['_kind']
    if kind == 'vs':
        add('fetch_embedded', i['fetch_embedded'])
        add('fetch_attrib_reg', i['fetch_attrib_reg'])
        add('fetch_buffer_reg', i['fetch_buffer_reg'])
        add('resources_num', i['resources_num'])
        add('scratch_size_dwords', i['scratch_size_dwords'])
        add('pa_cl_vs_out_cntl', i['pa_cl_vs_out_cntl'])
        add('clip.enabled', i['clip_enabled'])
        if i['clip_enabled']:
            for k, nm in (('clip_scale', 'clip.scale'), ('clip_offset', 'clip.offset'),
                          ('clip_half_extent', 'clip.half_extent')):
                for j, v in enumerate(i[k]):
                    add(nm, v, j)
        m = i['mesh']
        add('mesh.threads_num0', m['threads_num'][0])
        if m['threads_num'][0] != 0:
            for nm in ('wave_size', 'host_subgroup_size', 'lds_size_dwords', 'scratch_size_dwords',
                       'input_primitive', 'primitives_per_group', 'vertices_per_group', 'max_vertices',
                       'max_primitives', 'provoking_vertex'):
                add('mesh.' + nm, m[nm])
        for n in range(i['resources_num']):
            d = i['res_dst'][n]
            g = _res_derived(i['res_fields'][n])
            add('res.register_start', d[0], n)
            add('res.registers_num', d[1], n)
            add('res.fetch_index', d[3], n)
            add('res.attr_id', d[2], n)
            for nm in ('stride', 'swizzle_enabled', 'dstsel_x', 'dstsel_y', 'dstsel_z', 'dstsel_w',
                       'raw_format', 'out_of_bounds', 'add_tid'):
                add('res.' + nm, g[nm], n)
    elif kind == 'ps':
        for nm in ('scratch_size_dwords', 'input_num', 'ps_system_input_base', 'custom_interpolation_mask',
                   'ps_perspective_center_vgpr', 'ps_pos_x', 'ps_pos_y', 'ps_pos_z', 'ps_pos_w',
                   'ps_front_face', 'ps_ancillary', 'ps_no_perspective', 'ps_pixel_kill_enable',
                   'ps_depth_export_enable', 'ps_sample_mask_export_enable', 'ps_early_z'):
            add(nm, i[nm])
        for j, v in enumerate(i['target_output_mode']):
            add('target_output_mode', v, j)
        em = i['target_export_mapping']
        for base in (0, 4):                       # packed 4 slots per word, exactly like the key
            packed = 0
            for j in range(4):
                packed |= (em[base + j] & 0xFF) << (j * 8)
            add('target_export_mapping_word', packed, base // 4)
        for j in range(i['input_num']):
            add('interpolator_settings', i['interpolator_settings'][j], j)
    else:
        add('workgroup_register', i['workgroup_register'])
        add('wave_size', i['wave_size'])
        add('host_subgroup_size', i['host_subgroup_size'])
        add('thread_ids_num', i['thread_ids_num'])
        add('lds_size_dwords', i['lds_size_dwords'])
        add('scratch_size_dwords', i['scratch_size_dwords'])
        add('dispatch_thread_dimensions', i['dispatch_thread_dimensions'])
        for j in range(3):
            add('threads_num', i['threads_num'][j], j)
            add('group_id', i['group_id'][j], j)
        add('tg_size_en', i['tg_size_en'])
    return out


def key_values(rec: Record) -> tuple[int, ...]:
    return tuple(v for _, _, v in key_components(rec))


def valid_key(rec: Record) -> bool:
    """ValidKey(): the stored static key equals BuildStageStaticKey(decoded input info)."""
    return key_values(rec) == rec.key


def nonkey_components(rec: Record) -> list[tuple[str, int, int]]:
    """Recorded input fields that are NOT part of the static key (and whose values the compiler
    should not depend on for cache matching): raw descriptor words, stale clip data, etc."""
    i = rec.info
    out = []

    def add(name, value, idx=-1):
        out.append((name, idx, int(value) & M64))

    kind = i['_kind']
    if kind == 'vs':
        add('fetch_external', i['fetch_external'])
        if not i['clip_enabled']:
            for k, nm in (('clip_scale', 'clip.scale'), ('clip_offset', 'clip.offset'),
                          ('clip_half_extent', 'clip.half_extent')):
                for j, v in enumerate(i[k]):
                    add(nm + '(disabled)', v, j)
        m = i['mesh']
        add('mesh.threads_num1', m['threads_num'][1])
        add('mesh.threads_num2', m['threads_num'][2])
        if m['threads_num'][0] == 0:
            for nm in ('lds_size_dwords', 'scratch_size_dwords', 'host_subgroup_size', 'wave_size',
                       'input_primitive', 'primitives_per_group', 'vertices_per_group', 'max_vertices',
                       'max_primitives', 'provoking_vertex'):
                add('mesh.' + nm + '(non-mesh)', m[nm])
        for n in range(i['resources_num']):
            g = _res_derived(i['res_fields'][n])
            add('res.index_stride', g['index_stride'], n)
            add('res.num_records', g['num_records'], n)
            add('res.base48', g['base48'], n)
    elif kind == 'ps':
        add('lod_stats_subgroup', i['lod_stats_subgroup'])
        add('ps_sample_shading', i['ps_sample_shading'])
        add('ps_execute_on_noop', i['ps_execute_on_noop'])
        for j in range(i['input_num'], 32):
            add('interpolator_settings(beyond input_num)', i['interpolator_settings'][j], j)
    else:
        for j in range(3):
            add('dispatch_threads_num', i['dispatch_threads_num'][j], j)
    return out


# ---------------------------------------------------------------------------------------------
# Pipelines
# ---------------------------------------------------------------------------------------------
class Pipeline:
    __slots__ = ('index', 'vertex', 'pixel', 'compute', 'color_count', 'depth_format', 'stencil_format',
                 'color_formats', 'binding_count', 'attribute_count', 'bindings', 'attributes', 'state',
                 'nwords')

    @property
    def is_compute(self):
        return self.compute != NO_SHADER

    def rendering(self):
        return (self.color_count, self.depth_format, self.stencil_format, self.color_formats)

    def rendering_norm(self):
        """Rendering state as the hash uses it: only color_formats[:color_count] matter."""
        return (self.color_count, self.color_formats[:self.color_count], self.depth_format,
                self.stencil_format)

    def vertex_input(self):
        return (self.binding_count, self.attribute_count, self.bindings, self.attributes)

    def vertex_input_norm(self):
        return (self.binding_count, self.attribute_count, self.bindings[:self.binding_count],
                self.attributes[:self.attribute_count])


STATE_SCALARS = ('negative_one_to_one', 'depth_clip_enable', 'topology', 'primitive_restart_enable', 'samples',
                 'sample_shading_enable', 'depth_bounds_test_enable', 'depth_min_bounds',
                 'depth_max_bounds', 'stencil_test_enable')
STATE_ARRAYS = ('color_mask', 'color_srcblend', 'color_comb_fcn', 'color_destblend', 'alpha_srcblend',
                'alpha_comb_fcn', 'alpha_destblend', 'separate_alpha_blend', 'blend_enable', 'blend_bypass')
STATE_FIELDS_ORDER = (STATE_SCALARS + ('stencil_front', 'stencil_back', 'cull_front', 'cull_back', 'face',
                                       'provoking_vtx_last', 'polygon_mode') + STATE_ARRAYS)


def decode_pipeline(w, index: int = -1) -> Pipeline:
    """VisitPipeline(Reader&, PipelineRecord&) + the `reader.cursor == words.size()` check."""
    r = Rd([int(x) for x in np.asarray(w, dtype=np.uint32)])
    p = Pipeline()
    p.index = index
    p.nwords = len(w)
    p.vertex = r.u(); p.pixel = r.u(); p.compute = r.u()
    p.color_count = p.depth_format = p.stencil_format = 0
    p.color_formats = ()
    p.binding_count = p.attribute_count = 0
    p.bindings = p.attributes = ()
    p.state = {}
    if p.compute != NO_SHADER:
        if p.vertex != NO_SHADER or p.pixel != NO_SHADER:
            raise DecodeError('compute pipeline with vertex/pixel')
        if r.i != r.n:
            raise DecodeError('trailing words in compute pipeline')
        return p
    if p.vertex == NO_SHADER:
        raise DecodeError('graphics pipeline without vertex')
    p.color_count = r.u(); p.depth_format = r.u(); p.stencil_format = r.u()
    p.color_formats = tuple(r.arr(RENDER_COLOR_ATTACHMENTS_MAX))
    p.binding_count = r.u(); p.attribute_count = r.u()
    p.bindings = tuple((r.u(), r.b()) for _ in range(32))          # (stride, instance)
    p.attributes = tuple((r.u(), r.u()) for _ in range(32))        # (offset, binding)
    s = p.state
    s['negative_one_to_one'] = r.b(); s['depth_clip_enable'] = r.b(); s['topology'] = r.u()
    s['primitive_restart_enable'] = r.b(); s['samples'] = r.u(); s['sample_shading_enable'] = r.b()
    s['depth_bounds_test_enable'] = r.b(); s['depth_min_bounds'] = r.u(); s['depth_max_bounds'] = r.u()
    s['stencil_test_enable'] = r.b()
    for nm in ('stencil_front', 'stencil_back'):
        st = (r.u(), r.u(), r.u(), r.u())                            # fail, pass, depthFail, compare
        if any(v > 7 for v in st):
            raise DecodeError('stencil op/compare > 7')
        s[nm] = st
    s['cull_front'] = r.b(); s['cull_back'] = r.b(); s['face'] = r.b(); s['provoking_vtx_last'] = r.b()
    s['polygon_mode'] = r.u()
    s['color_mask'] = tuple(r.arr(8))
    for nm in ('color_srcblend', 'color_comb_fcn', 'color_destblend', 'alpha_srcblend', 'alpha_comb_fcn',
               'alpha_destblend'):
        arr = tuple(r.arr(8))
        if any(v > 255 for v in arr):
            raise DecodeError('uint8 field wider than 8 bits')
        s[nm] = arr
    for nm in ('separate_alpha_blend', 'blend_enable', 'blend_bypass'):
        s[nm] = tuple(r.barr(8))
    if r.i != r.n:
        raise DecodeError(f'{r.n - r.i} trailing words in pipeline')
    if p.color_count > RENDER_COLOR_ATTACHMENTS_MAX or p.binding_count > 32 or p.attribute_count > 32:
        raise DecodeError('count out of range')
    if s['topology'] > 10 or s['polygon_mode'] > 2:
        raise DecodeError('topology/polygon_mode out of range')
    if s['samples'] == 0 or s['samples'] > 64 or (s['samples'] & (s['samples'] - 1)):
        raise DecodeError('samples not a power of two <= 64')
    for k in range(p.attribute_count):
        if p.attributes[k][1] >= p.binding_count:
            raise DecodeError('attribute binding >= binding_count')
    for k in range(p.color_count):
        if (p.color_formats[k] == 0 or s['color_mask'][k] > 15 or s['color_srcblend'][k] > 20 or
                s['color_destblend'][k] > 20 or s['color_comb_fcn'][k] > 4 or s['alpha_srcblend'][k] > 20 or
                s['alpha_destblend'][k] > 20 or s['alpha_comb_fcn'][k] > 4):
            raise DecodeError('per-target state out of range')
    return p


def state_norm(p: Pipeline) -> tuple:
    """Static state as hashed (memcmp of the packed struct): every field, including stale slots
    beyond color_count."""
    s = p.state
    return tuple((k, s[k]) for k in STATE_FIELDS_ORDER)


# ---------------------------------------------------------------------------------------------
# Encoders (Visit(Writer&) / VisitPipeline(Writer&) transcribed a second time, used only to prove
# that decode is lossless: encode(decode(words)) == words, bit for bit)
# ---------------------------------------------------------------------------------------------
def encode_record(rec: Record) -> list[int]:
    out: list[int] = []
    u = out.append
    u(rec.stage); u(rec.hash & M32); u((rec.hash >> 32) & M32); u(rec.udc); u(rec.push)
    for blob in (rec.code, rec.back):
        n = len(blob) // 4
        u(n)
        out.extend(struct.unpack('<%dI' % n, blob))
    u(len(rec.key)); out.extend(rec.key)
    i = rec.info
    if rec.stage in (ST_VERTEX, ST_MESH):
        for k in ('resources_num', 'fetch_attrib_reg', 'fetch_buffer_reg'):
            u(i[k] & M32)
        u(i['scratch_size_dwords']); u(i['pa_cl_vs_out_cntl']); u(i['fetch_external']); u(i['fetch_embedded'])
        u(i['clip_enabled'])
        out.extend(i['clip_scale']); out.extend(i['clip_offset']); out.extend(i['clip_half_extent'])
        m = i['mesh']
        out.extend(m['threads_num'])
        for k in ('lds_size_dwords', 'scratch_size_dwords', 'host_subgroup_size', 'wave_size',
                  'input_primitive', 'primitives_per_group', 'vertices_per_group', 'max_vertices',
                  'max_primitives', 'provoking_vertex'):
            u(m[k])
        for f, d in zip(i['res_fields'], i['res_dst']):
            out.extend(f)
            out.extend(x & M32 for x in d)
    elif rec.stage == ST_PIXEL:
        for k in ('lod_stats_subgroup', 'input_num', 'ps_system_input_base', 'custom_interpolation_mask',
                  'ps_perspective_center_vgpr', 'scratch_size_dwords', 'ps_pos_x', 'ps_pos_y', 'ps_pos_z',
                  'ps_pos_w', 'ps_front_face', 'ps_ancillary', 'ps_no_perspective', 'ps_pixel_kill_enable',
                  'ps_depth_export_enable', 'ps_sample_mask_export_enable', 'ps_sample_shading',
                  'ps_early_z', 'ps_execute_on_noop'):
            u(i[k])
        out.extend(i['interpolator_settings']); out.extend(i['target_output_mode'])
        out.extend(i['target_export_mapping'])
    else:
        out.extend(i['threads_num'])
        for k in ('lds_size_dwords', 'scratch_size_dwords', 'host_subgroup_size', 'wave_size'):
            u(i[k])
        out.extend(i['dispatch_threads_num']); out.extend(i['group_id'])
        u(i['dispatch_thread_dimensions']); u(i['thread_ids_num'] & M32); u(i['workgroup_register'] & M32)
        u(i['tg_size_en'])
    u(len(rec.buffers))
    for b in rec.buffers:
        out.extend(b)
    u(len(rec.images))
    for im in rec.images:
        out.extend(im)
    return out


def encode_pipeline(p: Pipeline) -> list[int]:
    out: list[int] = [p.vertex, p.pixel, p.compute]
    if p.compute != NO_SHADER:
        return out
    out += [p.color_count, p.depth_format, p.stencil_format]
    out += list(p.color_formats)
    out += [p.binding_count, p.attribute_count]
    for b in p.bindings:
        out += list(b)
    for a in p.attributes:
        out += list(a)
    s = p.state
    for k in STATE_SCALARS:
        out.append(s[k])
    for k in ('stencil_front', 'stencil_back'):
        out += list(s[k])
    for k in ('cull_front', 'cull_back', 'face', 'provoking_vtx_last', 'polygon_mode'):
        out.append(s[k])
    for k in STATE_ARRAYS:
        out += list(s[k])
    return out


# ---------------------------------------------------------------------------------------------
# File
# ---------------------------------------------------------------------------------------------
class WarmFile:
    def __init__(self, path: Path):
        self.path = Path(path)
        self.stat_mtime = self.path.stat().st_mtime
        self.size = self.path.stat().st_size
        self.identity = ''
        self.checksum_stored = None
        self.checksum_calc = None
        self.records: list[Record] = []
        self.pipelines: list[Pipeline] = []
        self.rec_fail: list[tuple[int, str]] = []
        self.pipe_fail: list[tuple[int, str]] = []
        self.key_mismatch: list[int] = []
        self.pipe_semantic_fail: list[tuple[int, str]] = []
        self.rec_words_total = 0
        self.rec_roundtrip_fail: list[int] = []
        self.pipe_roundtrip_fail: list[int] = []
        self.pipe_words_total = 0
        self.n_records_declared = 0
        self.n_pipelines_declared = 0
        self.file_consumed_exactly = False
        self.dir_id = self.path.parent.name

    def load(self, verify_checksum: bool = True):
        raw = self.path.read_bytes()
        nl = raw.index(b'\n', 0, 1024)
        self.identity = raw[:nl + 1].decode('latin1')
        self.checksum_stored = struct.unpack_from('<Q', raw, nl + 1)[0]
        body = raw[nl + 1 + 8:]
        if len(body) % 4:
            raise DecodeError('body is not a multiple of 4 bytes')
        if verify_checksum:
            self.checksum_calc = xxh3_64(body)
        words = np.frombuffer(body, dtype='<u4')
        top = Rd(words)
        nrec = top.u()
        self.n_records_declared = nrec
        rec_words = [top.words(1024 * 1024) for _ in range(nrec)]
        npipe = top.u()
        self.n_pipelines_declared = npipe
        pipe_words = [top.words(4096) for _ in range(npipe)]
        self.file_consumed_exactly = (top.i == top.n)
        for idx, w in enumerate(rec_words):
            self.rec_words_total += len(w)
            try:
                rec = decode_record(w, idx)
            except (DecodeError, IndexError, ValueError) as e:
                self.rec_fail.append((idx, str(e)))
                rec = None
            if rec is not None and not valid_key(rec):
                self.key_mismatch.append(idx)
            if rec is not None and encode_record(rec) != [int(x) for x in w]:
                self.rec_roundtrip_fail.append(idx)
            self.records.append(rec)
        for idx, w in enumerate(pipe_words):
            self.pipe_words_total += len(w)
            try:
                p = decode_pipeline(w, idx)
            except (DecodeError, IndexError, ValueError) as e:
                self.pipe_fail.append((idx, str(e)))
                p = None
            if p is not None and encode_pipeline(p) != [int(x) for x in w]:
                self.pipe_roundtrip_fail.append(idx)
            self.pipelines.append(p)
        # ValidPipeline(): stage of the referenced records + vertex attribute-count rule
        for p in self.pipelines:
            if p is None:
                continue

            def stage(ix):
                return self.records[ix].stage if ix < len(self.records) and self.records[ix] else ST_UNKNOWN
            if p.compute != NO_SHADER:
                if stage(p.compute) != ST_COMPUTE:
                    self.pipe_semantic_fail.append((p.index, 'compute ref is not a compute record'))
                continue
            sv = stage(p.vertex)
            if sv not in (ST_VERTEX, ST_MESH):
                self.pipe_semantic_fail.append((p.index, 'vertex ref is not VS/MS'))
                continue
            if p.pixel != NO_SHADER and stage(p.pixel) != ST_PIXEL:
                self.pipe_semantic_fail.append((p.index, 'pixel ref is not PS'))
                continue
            vr = self.records[p.vertex]
            if sv == ST_MESH:
                ok = p.attribute_count == 0 and p.binding_count == 0
            else:
                ok = p.attribute_count == vr.info['resources_num']
            if not ok:
                self.pipe_semantic_fail.append((p.index, 'vertex_input attribute_count rule'))
        return self

    def ok_records(self):
        return [r for r in self.records if r is not None]

    def ok_pipelines(self):
        return [p for p in self.pipelines if p is not None]


def find_files() -> list[Path]:
    """Cache files ordered oldest -> newest by mtime."""
    files = sorted(CACHE_ROOT.glob('*/PPSA01341.shaders'), key=lambda p: p.stat().st_mtime)
    return files


def load_all(verify_checksum: bool = True) -> list[WarmFile]:
    return [WarmFile(p).load(verify_checksum) for p in find_files()]


if __name__ == '__main__':
    for f in load_all():
        print(f.path, f.size, 'records', len(f.records), 'pipelines', len(f.pipelines))

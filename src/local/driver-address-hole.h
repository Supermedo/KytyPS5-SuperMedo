#pragma once
// DLSS frame generation (KYTY_FRAMEGEN) on Windows: the guest reservation stops at a ceiling to leave
// host address space below 1 TiB to the NVIDIA driver, which reserves tens of GiB there when the
// Vulkan device is created (optical flow, NGX interop). The executable has no bottom-up
// randomization, so whatever came first filled that room: the direct-memory view (13.5 GiB) and the
// 256 GiB the driver itself reserves at vkCreateInstance (driver 617.14), and vkCreateDevice failed
// with VK_ERROR_INITIALIZATION_FAILED. The room is now held reserved from the guest reservation on
// and released just before vkCreateDevice; the earlier allocations land above 1 TiB instead.

#include <algorithm>
#include <cstdint>
#include <cstdio>
#include <utility>
#include <vector>

#if defined(_WIN32)
#include <windows.h> // IWYU pragma: keep
#endif

namespace LocalDriverHole {

inline std::vector<std::pair<uint64_t, uint64_t>>& Regions() {
	static std::vector<std::pair<uint64_t, uint64_t>> regions;
	return regions;
}

// Reserves the free parts of [begin, end) (64 KiB granularity).
inline void Hold(uint64_t begin, uint64_t end) {
#if defined(_WIN32)
	constexpr uint64_t Granularity = 0x10000;
	uint64_t           held        = 0;
	for (uint64_t at = begin; at < end;) {
		MEMORY_BASIC_INFORMATION info {};
		if (VirtualQuery(reinterpret_cast<const void*>(at), &info, sizeof(info)) == 0) break;
		const auto region_base = reinterpret_cast<uint64_t>(info.BaseAddress);
		const auto region_end  = std::min<uint64_t>(end, region_base + static_cast<uint64_t>(info.RegionSize));
		const auto start       = (std::max<uint64_t>(region_base, at) + Granularity - 1) & ~(Granularity - 1);
		const auto stop        = region_end & ~(Granularity - 1);
		if (info.State == MEM_FREE && stop > start &&
		    VirtualAlloc(reinterpret_cast<void*>(start), stop - start, MEM_RESERVE, PAGE_NOACCESS) != nullptr) {
			Regions().emplace_back(start, stop - start);
			held += stop - start;
		}
		if (region_end <= at) break;
		at = region_end;
	}
	std::printf("Frame generation: holding %llu MiB of address space for the driver\n",
	            static_cast<unsigned long long>(held >> 20u));
	std::fflush(stdout);
#else
	(void)begin;
	(void)end;
#endif
}

// Gives the held room back (once, just before vkCreateDevice).
inline void Release() {
#if defined(_WIN32)
	for (const auto& [base, size]: Regions()) {
		VirtualFree(reinterpret_cast<void*>(base), 0, MEM_RELEASE);
	}
#endif
	Regions().clear();
}

} // namespace LocalDriverHole

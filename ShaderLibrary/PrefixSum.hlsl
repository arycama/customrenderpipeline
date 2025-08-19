// Simple parallel-prefix sum. Requires a read/write function to be defined by caller. (Should write to/from groupshared memory)
// Can optionally output the total sum of the group if required. (Eg indirect draw args, multi-pass prefix sum)
// Note that a GroupMemoryBarrierWithGroupSync may be required. This is left out incase the caller does not require it.
void PrefixSumSharedWrite(uint index, uint data); // array[index] = data
uint PrefixSumSharedRead(uint index); // return array[index];

#define NUM_BANKS 16
#define LOG_NUM_BANKS 4
#define CONFLICT_FREE_OFFSET(n)((n) >> NUM_BANKS + (n) >> (2 * LOG_NUM_BANKS))

void PrefixSum(uint groupIndex, uint size, uint log2Size, out uint totalSum)
{
	// Perform reduction
	[unroll]
	for (uint i = 0; i < log2Size; i++)
	{
		GroupMemoryBarrierWithGroupSync();
		
		if (groupIndex >= size >> (i + 1))
			continue;
		
		uint offset = 1 << i;
		uint ai = offset * (2 * groupIndex + 1) - 1;
		uint bi = offset * (2 * groupIndex + 2) - 1;
		PrefixSumSharedWrite(bi, PrefixSumSharedRead(bi) + PrefixSumSharedRead(ai));
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	// Clear the last element
	totalSum = 0; // Only initialized to avoid uninitialized variable warnings
	if (!groupIndex)
	{
		totalSum = PrefixSumSharedRead(size - 1);
		PrefixSumSharedWrite(size - 1, 0);
	}
		
	// Perform downsweep and build scan
	[unroll]
	for (i = 0; i < log2Size; i++)
	{
		GroupMemoryBarrierWithGroupSync();

		if (groupIndex >= (1 << i))
			continue;
			
		uint offset = size >> (i + 1);
		uint ai = offset * (2 * groupIndex + 1) - 1;
		uint bi = offset * (2 * groupIndex + 2) - 1;
		uint t0 = PrefixSumSharedRead(ai);
		uint t1 = PrefixSumSharedRead(bi);
		PrefixSumSharedWrite(ai, t1);
		PrefixSumSharedWrite(bi, t0 + t1);
	}
}

// Overload that doesn't output the total sum
void PrefixSum(uint groupIndex, uint size, uint log2Size)
{
	uint totalSum;
	PrefixSum(groupIndex, size, log2Size, totalSum);
}
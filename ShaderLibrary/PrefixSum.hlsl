// Simple parallel-prefix sum. Requires a read/write function to be defined by caller. (Should write to/from groupshared memory)
// Can optionally output the total sum of the group if required. (Eg indirect draw args, multi-pass prefix sum)
// Note that a GroupMemoryBarrierWithGroupSync may be required. This is left out incase the caller does not require it.
void PrefixSumSharedWrite(uint index, uint data); // array[index] = data
uint PrefixSumSharedRead(uint index); // return array[index];

const static uint NumBanks = 16;
const static uint LogNumBanks = firstbitlow(NumBanks);
uint ConflictFreeOffset(uint n) { return n >> (NumBanks + (n >> (2u * LogNumBanks))); }
uint ConflictFreeIndex(uint n) { return n + ConflictFreeOffset(n); }

groupshared uint SharedTotalSum;

uint PrefixSum(uint value, uint groupIndex, uint size, out uint totalSum)
{
	PrefixSumSharedWrite(groupIndex + ConflictFreeOffset(groupIndex), value);

	uint offset = 1;
	
	// build sum in place up the tree
	[unroll]
	for (uint d = size >> 1; d > 0; d >>= 1)
	{
		GroupMemoryBarrierWithGroupSync();
		
		if (groupIndex < d)
		{
			// B
			uint ai = offset * (2 * groupIndex + 1) - 1;
			uint bi = offset * (2 * groupIndex + 2) - 1;
			ai += ConflictFreeOffset(ai);
			bi += ConflictFreeOffset(bi);
			PrefixSumSharedWrite(bi, PrefixSumSharedRead(ai) + PrefixSumSharedRead(bi));
		}
		
		offset *= 2;
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	// C: clear the last element
	if (!groupIndex)
	{
		SharedTotalSum = PrefixSumSharedRead(size - 1 + ConflictFreeOffset(size - 1));
		PrefixSumSharedWrite(size - 1 + ConflictFreeOffset(size - 1), 0);
	}
	
	// traverse down tree & build scan
	[unroll]
	for (d = 1; d < size; d *= 2)
	{
		offset >>= 1;
		GroupMemoryBarrierWithGroupSync();

		if (groupIndex < d)
		{
			// D
			uint ai = offset * (2 * groupIndex + 1) - 1;
			uint bi = offset * (2 * groupIndex + 2) - 1;
			ai += ConflictFreeOffset(ai);
			bi += ConflictFreeOffset(bi);
			uint t = PrefixSumSharedRead(ai);
			PrefixSumSharedWrite(ai, PrefixSumSharedRead(bi));
			PrefixSumSharedWrite(bi, PrefixSumSharedRead(bi) + t);
		}
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	totalSum = SharedTotalSum;
	
	return PrefixSumSharedRead(groupIndex);
}

uint PrefixSum(uint value, uint groupIndex, uint size)
{
	uint totalSum;
	return PrefixSum(value, groupIndex, size, totalSum);
}
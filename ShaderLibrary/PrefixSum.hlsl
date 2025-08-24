// Simple parallel-prefix sum. Requires a read/write function to be defined by caller. (Should write to/from groupshared memory)
// Can optionally output the total sum of the group if required. (Eg indirect draw args, multi-pass prefix sum)
// Note that a GroupMemoryBarrierWithGroupSync may be required. This is left out incase the caller does not require it.
void PrefixSumSharedWrite(uint index, uint data); // array[index] = data
uint PrefixSumSharedRead(uint index); // return array[index];
void PrefixSumOutputTotalCount(uint data);

void PrefixSum(uint groupIndex, uint size)
{
	uint offset = 1;
	
	// build sum in place up the tree
	for (uint d = size >> 1; d > 0; d >>= 1)
	{
		GroupMemoryBarrierWithGroupSync();
		
		if (groupIndex < d)
		{
			// B
			uint ai = offset * (2 * groupIndex + 1) - 1;
			uint bi = offset * (2 * groupIndex + 2) - 1;
			PrefixSumSharedWrite(bi, PrefixSumSharedRead(ai) + PrefixSumSharedRead(bi));
		}
		
		offset *= 2;
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	// C: clear the last element
	if (!groupIndex)
	{
		PrefixSumOutputTotalCount(PrefixSumSharedRead(size - 1));
		PrefixSumSharedWrite(size - 1, 0);
	}
	
	// traverse down tree & build scan
	for (d = 1; d < size; d *= 2)
	{
		offset >>= 1;
		GroupMemoryBarrierWithGroupSync();

		if (groupIndex < d)
		{
			// D
			uint ai = offset * (2 * groupIndex + 1) - 1;
			uint bi = offset * (2 * groupIndex + 2) - 1;
			uint t = PrefixSumSharedRead(ai);
			PrefixSumSharedWrite(ai, PrefixSumSharedRead(bi));
			PrefixSumSharedWrite(bi, PrefixSumSharedRead(bi) + t);
		}
	}
	
	GroupMemoryBarrierWithGroupSync();
}
// Simple parallel-prefix sum. Requires a read/write function to be defined by caller. (Should write to/from groupshared memory)
// Can optionally output the total sum of the group if required. (Eg indirect draw args, multi-pass prefix sum)
// Note that a GroupMemoryBarrierWithGroupSync may be required. This is left out incase the caller does not require it.
void PrefixSumSharedWrite(uint index, uint data); // array[index] = data
uint PrefixSumSharedRead(uint index); // return array[index];

const static uint NumBanks = 16;
const static uint LogNumBanks = firstbitlow(NumBanks);
groupshared uint SharedTotalSum;
groupshared uint4 SharedTotalSum4;

uint ConflictFreeOffset(uint n)
{
	return n >> (NumBanks + (n >> (2u * LogNumBanks)));
}

uint ConflictFreeIndex(uint n)
{
	return n + ConflictFreeOffset(n);
}

uint SharedRead(uint index)
{
	return PrefixSumSharedRead(ConflictFreeIndex(index));
}

void SharedWrite(uint index, uint data)
{
	PrefixSumSharedWrite(ConflictFreeIndex(index), data);
}

uint PrefixSum(uint value, uint groupIndex, uint size, out uint totalSum)
{
	SharedWrite(groupIndex, value);

	uint offset = 1;
	
	// Build sum in place up the tree
	[unroll]
	for (uint d = size >> 1; d > 0; d >>= 1, offset <<= 1)
	{
		GroupMemoryBarrierWithGroupSync();
		if (groupIndex >= d)
			continue;
			
		uint ai = offset * (2 * groupIndex + 1) - 1;
		uint bi = offset * (2 * groupIndex + 2) - 1;
		SharedWrite(bi, SharedRead(ai) + SharedRead(bi));
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	// Clear the last element
	if (!groupIndex)
	{
		SharedTotalSum = SharedRead(size - 1);
		SharedWrite(size - 1, 0);
	}
	
	// Traverse down tree & build scan
	[unroll]
	for (d = 1; d < size; d <<= 1)
	{
		offset >>= 1;
		GroupMemoryBarrierWithGroupSync();

		if (groupIndex >= d)
			continue;
			
		uint ai = offset * (2 * groupIndex + 1) - 1;
		uint bi = offset * (2 * groupIndex + 2) - 1;
		uint t = SharedRead(ai);
		uint b = SharedRead(bi);
		SharedWrite(ai, b);
		SharedWrite(bi, b + t);
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	totalSum = SharedTotalSum;
	return SharedRead(groupIndex);
}

void PrefixSumSharedWrite4(uint index, uint4 data); // array[index] = data
uint4 PrefixSumSharedRead4(uint index); // return array[index];

uint4 SharedRead4(uint index)
{
	return PrefixSumSharedRead4(ConflictFreeIndex(index));
}

void SharedWrite4(uint index, uint4 data)
{
	PrefixSumSharedWrite4(ConflictFreeIndex(index), data);
}

uint4 PrefixSum(uint4 value, uint groupIndex, uint size, out uint4 totalSum)
{
	SharedWrite4(groupIndex, value);

	uint offset = 1;
	
	// Build sum in place up the tree
	[unroll]
	for (uint d = size >> 1; d > 0; d >>= 1, offset <<= 1)
	{
		GroupMemoryBarrierWithGroupSync();
		if (groupIndex >= d)
			continue;
			
		uint ai = offset * (2 * groupIndex + 1) - 1;
		uint bi = offset * (2 * groupIndex + 2) - 1;
		SharedWrite4(bi, SharedRead4(ai) + SharedRead4(bi));
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	// Clear the last element
	if (!groupIndex)
	{
		SharedTotalSum4 = SharedRead4(size - 1);
		SharedWrite4(size - 1, 0);
	}
	
	// Traverse down tree & build scan
	[unroll]
	for (d = 1; d < size; d <<= 1)
	{
		offset >>= 1;
		GroupMemoryBarrierWithGroupSync();

		if (groupIndex >= d)
			continue;
			
		uint ai = offset * (2 * groupIndex + 1) - 1;
		uint bi = offset * (2 * groupIndex + 2) - 1;
		uint4 t = SharedRead4(ai);
		uint4 b = SharedRead4(bi);
		SharedWrite4(ai, b);
		SharedWrite4(bi, b + t);
	}
	
	GroupMemoryBarrierWithGroupSync();
	
	totalSum = SharedTotalSum;
	return SharedRead4(groupIndex);
}
/// The number of bits we are sorting per pass. 
/// Changing this value requires
/// internal changes in LDS distribution and count, 
/// reduce, scan, and scatter passes
#define BitsPerPass		    4

/// The number of bins used for the counting phase
/// of the algorithm. Changing this value requires
/// internal changes in LDS distribution and count, 
/// reduce, scan, and scatter passes
#define	SortBinCount			    (1 << BitsPerPass)

/// The number of threads to execute in parallel
/// for each dispatch group
#define ThreadGroupSize		    128

uint NumKeys; ///< The number of keys to sort
int NumBlocksPerThreadGroup; ///< How many blocks of keys each thread group needs to process
uint NumThreadGroups; ///< How many thread groups are being run concurrently for sort
uint NumThreadGroupsWithAdditionalBlocks; ///< How many thread groups need to process additional block data
uint NumReduceThreadgroupPerBin; ///< How many thread groups are summed together for each reduced bin entry
uint NumScanValues; ///< How many values to perform scan prefix (+ add) on
uint Shift; ///< What bits are being sorted (4 bit increments)
uint Padding; ///< Padding - unused

StructuredBuffer<uint> SourceKeys, ScanSource, ScanScratch, SourcePayload;
RWStructuredBuffer<uint> SumTable, ReduceTable, ScanDest, DestKey, DestPayload;

groupshared uint LDSSums[ThreadGroupSize];
uint ParallelSortBlockScanPrefix(uint localSum, uint localID)
{
    // Do wave local scan-prefix
    uint wavePrefixed = WavePrefixSum(localSum);

    // Since we are dealing with thread group sizes greater than HW wave size, we need to account for what wave we are in.
    uint waveID = localID / WaveGetLaneCount();
    uint laneID = WaveGetLaneIndex();

    // Last element in a wave writes out partial sum to LDS
    if (laneID == WaveGetLaneCount() - 1)
        LDSSums[waveID] = wavePrefixed + localSum;

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // First wave prefixes partial sums
    if (!waveID)
        LDSSums[localID] = WavePrefixSum(LDSSums[localID]);

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Add the partial sums back to each wave prefix
    wavePrefixed += LDSSums[waveID];

    return wavePrefixed;
}

// Local histogram for offset calculations
groupshared uint LocalHistogram[SortBinCount];
// Scratch area for algorithm
groupshared uint LDSScratch[ThreadGroupSize];

void ParallelSortScatterUInt(uint localID, uint groupID, uint ShiftBit)
{
    // Count value occurences
	uint srcKey = (localID < NumKeys ? SourceKeys[localID] : 0xffffffff);
	uint srcValue = (localID < NumKeys ? SourcePayload[localID] : 0);

    // Clear the local histogram
    LocalHistogram[localID] = 0;

    // Sort the keys locally in LDS
    for (uint bitShift = 0; bitShift < BitsPerPass; bitShift += 2)
    {
        // Figure out the keyIndex
		uint keyIndex = (srcKey >> ShiftBit) & 0xf;
        uint bitKey = (keyIndex >> bitShift) & 0x3;

        // Create a packed histogram 
        uint packedHistogram = 1 << (bitKey * 8);

        // Sum up all the packed keys (generates counted offsets up to current thread group)
        uint localSum = ParallelSortBlockScanPrefix(packedHistogram, localID);

        // Last thread stores the updated histogram counts for the thread group
        // Scratch = 0xsum3|sum2|sum1|sum0 for thread group
        if (localID == (ThreadGroupSize - 1))
            LDSScratch[0] = localSum + packedHistogram;

        // Wait for everyone to catch up
        GroupMemoryBarrierWithGroupSync();

        // Load the sums value for the thread group
        packedHistogram = LDSScratch[0];

        // Add prefix offsets for all 4 bit "keys" (packedHistogram = 0xsum2_1_0|sum1_0|sum0|0)
        packedHistogram = (packedHistogram << 8) + (packedHistogram << 16) + (packedHistogram << 24);

        // Calculate the proper offset for this thread's value
        localSum += packedHistogram;

        // Calculate target offset
        uint keyOffset = (localSum >> (bitKey * 8)) & 0xff;

        // Re-arrange the keys (store, sync, load)
		LDSSums[keyOffset] = srcKey;
        GroupMemoryBarrierWithGroupSync();
		srcKey = LDSSums[localID];

        // Wait for everyone to catch up
        GroupMemoryBarrierWithGroupSync();

        // Re-arrange the values if we have them (store, sync, load)
		LDSSums[keyOffset] = srcValue;
        GroupMemoryBarrierWithGroupSync();
		srcValue = LDSSums[localID];

        // Wait for everyone to catch up
        GroupMemoryBarrierWithGroupSync();
    }

    // Need to recalculate the keyIndex on this thread now that values have been copied around the thread group
	uint keyIndex = (srcKey >> ShiftBit) & 0xf;

    // Reconstruct histogram
    InterlockedAdd(LocalHistogram[keyIndex], 1);

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Prefix histogram
    uint histogramPrefixSum = WavePrefixSum(localID < SortBinCount ? LocalHistogram[localID] : 0);

    // Broadcast prefix-sum via LDS
    if (localID < SortBinCount)
        LDSScratch[localID] = histogramPrefixSum;

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Get the local offset (at this point the keys are all in increasing order from 0 -> num bins in localID 0 -> thread group size)
	uint totalOffset = localID - LDSScratch[keyIndex];

    if (totalOffset >= NumKeys)
		return;
        
	DestKey[totalOffset] = srcKey;
	DestPayload[totalOffset] = srcValue;
}
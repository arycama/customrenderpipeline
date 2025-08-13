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

groupshared uint Histogram[ThreadGroupSize * SortBinCount];
void ParallelSortCountUInt(uint localID, uint groupID, uint ShiftBit)
{
    // Start by clearing our local counts in LDS
    for (int i = 0; i < SortBinCount; i++)
        Histogram[(i * ThreadGroupSize) + localID] = 0;

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Data is processed in blocks, and how many we process can changed based on how much data we are processing
    // versus how many thread groups we are processing with
    int BlockSize = ThreadGroupSize;

    // Figure out this thread group's index into the block data (taking into account thread groups that need to do extra reads)
    uint ThreadgroupBlockStart = (BlockSize * NumBlocksPerThreadGroup * groupID);
    uint NumBlocksToProcess = NumBlocksPerThreadGroup;

    if (groupID >= NumThreadGroups - NumThreadGroupsWithAdditionalBlocks)
    {
        ThreadgroupBlockStart += (groupID - (NumThreadGroups - NumThreadGroupsWithAdditionalBlocks)) * BlockSize;
        NumBlocksToProcess++;
    }

    // Get the block start index for this thread
    uint BlockIndex = ThreadgroupBlockStart + localID;

    // Count value occurrence
    for (uint BlockCount = 0; BlockCount < NumBlocksToProcess; BlockCount++, BlockIndex += BlockSize)
    {
        // Pre-load the key values in order to hide some of the read latency
        uint srcKey = (BlockIndex < NumKeys ? SourceKeys[BlockIndex] : 0xffffffff);

		if (BlockIndex < NumKeys)
        {
			uint localKey = (srcKey >> ShiftBit) & 0xf;
            InterlockedAdd(Histogram[(localKey * ThreadGroupSize) + localID], 1);
			BlockIndex += ThreadGroupSize;
		}
    }

    // Even though our LDS layout guarantees no collisions, our thread group size is greater than a wave
    // so we need to make sure all thread groups are done counting before we start tallying up the results
    GroupMemoryBarrierWithGroupSync();

    if (localID < SortBinCount)
    {
        uint sum = 0;
        for (int i = 0; i < ThreadGroupSize; i++)
        {
            sum += Histogram[localID * ThreadGroupSize + i];
        }
		SumTable[localID * NumThreadGroups + groupID] = sum;
	}
}

groupshared uint LDSSums[ThreadGroupSize];
uint ParallelSortThreadgroupReduce(uint localSum, uint localID)
{
    // Do wave local reduce
    uint waveReduced = WaveActiveSum(localSum);
        
    // First lane in a wave writes out wave reduction to LDS (this accounts for num waves per group greater than HW wave size)
    // Note that some hardware with very small HW wave sizes (i.e. <= 8) may exhibit issues with this algorithm, and have not been tested.
    uint waveID = localID / WaveGetLaneCount();
    if (WaveIsFirstLane())
        LDSSums[waveID] = waveReduced;

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // First wave worth of threads sum up wave reductions
    if (!waveID)
        waveReduced = WaveActiveSum((localID < ThreadGroupSize / WaveGetLaneCount()) ? LDSSums[localID] : 0);

    // Returned the reduced sum
    return waveReduced;
}

void ParallelSortReduceCount(uint localID, uint groupID)
{
    // Figure out what bin data we are reducing
    uint BinID = groupID / NumReduceThreadgroupPerBin;
    uint BinOffset = BinID * NumThreadGroups;

    // Get the base index for this thread group
    uint BaseIndex = (groupID % NumReduceThreadgroupPerBin) * ThreadGroupSize;

    // Calculate partial sums for entries this thread reads in
    uint DataIndex = BaseIndex + localID;
	uint threadgroupSum = (DataIndex < NumThreadGroups) ? SumTable[BinOffset + DataIndex] : 0;

    // Reduce across the entirety of the thread group
    threadgroupSum = ParallelSortThreadgroupReduce(threadgroupSum, localID);

    // First thread of the group writes out the reduced sum for the bin
    if (localID == 0)
        ReduceTable[groupID] = threadgroupSum;

    // What this will look like in the reduced table is:
    //	[ [bin0 ... bin0] [bin1 ... bin1] ... ]
}

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

// This is to transform uncoalesced loads into coalesced loads and 
// then scattered loads from LDS
groupshared uint LDS[ThreadGroupSize];
void ParallelSortScanPrefix(uint numValuesToScan, uint localID, uint groupID, uint BinOffset, uint BaseIndex, bool AddPartialSums)
{
    // Perform coalesced loads into LDS
    uint DataIndex = BaseIndex + localID;

	LDS[localID] = (DataIndex < numValuesToScan) ? ScanSource[BinOffset + DataIndex] : 0;

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Calculate the local scan-prefix for current thread
    uint tmp = LDS[localID];
    LDS[localID] = 0;
    uint threadgroupSum = tmp;

    // Scan prefix partial sums
    threadgroupSum = ParallelSortBlockScanPrefix(threadgroupSum, localID);

    // Add reduced partial sums if requested
    uint partialSum = 0;
    if (AddPartialSums)
    {
        // Partial sum additions are a little special as they are tailored to the optimal number of 
        // thread groups we ran in the beginning, so need to take that into account
        partialSum = ScanScratch[groupID];
    }

    // Add the block scanned-prefixes back in
    LDS[localID] += threadgroupSum;

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Perform coalesced writes to scan dst
	if (BaseIndex + localID < numValuesToScan)
		ScanDest[BinOffset + BaseIndex + localID] = LDS[localID] + partialSum;
}

// Offset cache to avoid loading the offsets all the time
groupshared uint BinOffsetCache[ThreadGroupSize];
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

    uint globalOffset = BinOffsetCache[keyIndex];

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Get the local offset (at this point the keys are all in increasing order from 0 -> num bins in localID 0 -> thread group size)
    uint localOffset = localID - LDSScratch[keyIndex];

    // Write to destination
    uint totalOffset = globalOffset + localOffset;

    if (totalOffset < NumKeys)
    {
		DestKey[totalOffset] = srcKey;
		DestPayload[totalOffset] = srcValue;
	}

    // Wait for everyone to catch up
    GroupMemoryBarrierWithGroupSync();

    // Update the cached histogram for the next set of entries
    if (localID < SortBinCount)
        BinOffsetCache[localID] += LocalHistogram[localID];
}
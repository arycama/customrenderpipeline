#ifndef SAMPLERS_INCLUDED
#define SAMPLERS_INCLUDED

SamplerState PointClampSampler, PointRepeatSampler, LinearClampSampler, LinearRepeatSampler, TrilinearClampSampler, TrilinearRepeatSampler, TrilinearRepeatAniso4Sampler, TrilinearRepeatAniso16Sampler;

const static SamplerState SurfaceSampler = TrilinearRepeatAniso4Sampler;

#endif
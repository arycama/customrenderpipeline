#pragma once

SamplerState PointClampSampler, PointRepeatSampler, LinearClampSampler, LinearRepeatSampler, TrilinearClampSampler, TrilinearRepeatSampler, TrilinearRepeatAniso4Sampler, TrilinearRepeatAniso16Sampler;

const static SamplerState SurfaceSampler = TrilinearRepeatAniso4Sampler;
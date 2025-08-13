#pragma once

SamplerState PointClampSampler, PointRepeatSampler, LinearClampSampler, LinearRepeatSampler, TrilinearClampSampler, TrilinearRepeatSampler, TrilinearRepeatAniso16Sampler;

const static SamplerState SurfaceSampler = TrilinearRepeatAniso16Sampler;
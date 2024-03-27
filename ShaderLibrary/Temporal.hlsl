#ifndef TEMPORAL_INCLUDED
#define TEMPORAL_INCLUDED

cbuffer TemporalProperties
{
	float4 _Jitter;
	
	float _MaxCrossWeight;
	float _MaxBoxWeight;
	float _CenterCrossFilterWeight;
	float _CenterBoxFilterWeight;
	
	float4 _CrossFilterWeights;
	float4 _BoxFilterWeights0;
	float4 _BoxFilterWeights1;
	
};

#endif
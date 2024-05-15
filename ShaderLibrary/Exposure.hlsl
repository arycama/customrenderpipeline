#ifndef EXPOSURE_INCLUDED
#define EXPOSURE_INCLUDED

cbuffer Exposure
{
	float _Exposure;
	float _RcpExposure;
	float _PreviousToCurrentExposure;
	float _CurrentToPreviousExposure;
};

#endif
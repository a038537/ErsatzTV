﻿using ErsatzTV.FFmpeg.State;

namespace ErsatzTV.FFmpeg.Filter.Cuda;

public class OverlayCudaFilter : OverlayFilter
{
    public OverlayCudaFilter(FrameState currentState, WatermarkState watermarkState, FrameSize resolution) : base(
        currentState,
        watermarkState,
        resolution)
    {
    }

    public override FrameState NextState(FrameState currentState) => currentState;

    public override string Filter => $"overlay_cuda={Position}";
}
﻿namespace ErsatzTV.Application.Playouts
{
    public record PlayoutNameViewModel(
        int PlayoutId,
        string ChannelName,
        string ChannelNumber,
        string ScheduleName);
}
﻿using ErsatzTV.FFmpeg.Environment;

namespace ErsatzTV.FFmpeg.Encoder;

public abstract class EncoderBase : IEncoder
{
    public IList<EnvironmentVariable> EnvironmentVariables => Array.Empty<EnvironmentVariable>();
    public IList<string> GlobalOptions => Array.Empty<string>();
    public IList<string> InputOptions(InputFile inputFile) => Array.Empty<string>();
    public IList<string> FilterOptions => Array.Empty<string>();
    public virtual IList<string> OutputOptions => new List<string> { Kind == StreamKind.Video ? "-c:v" : "-c:a", Name };
    public virtual FrameState NextState(FrameState currentState) => currentState;

    public abstract string Name { get; }
    public abstract StreamKind Kind { get; }
    public virtual string Filter => string.Empty;
}
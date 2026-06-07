# VMXBench - VMX Codec Benchmark Tool

An independent, standalone benchmark application for measuring the real encode behavior of the libvmx codec core.

## Purpose

VMXBench is designed to:
- Measure actual 4K frame encode output sizes
- Test the impact of quality, frameMax, and minQuality parameters on encoded output
- Evaluate codec performance across different pixel formats (UYVY, P216)
- Generate synthetic test frames with various characteristics
- Produce detailed CSV and log reports for analysis

## Building the Project

### Prerequisites
- .NET 6.0 or later
- libvmx.dll (native VMX codec library) - must be available on PATH or in the working directory

### Build Command

From the workspace root:

```bash
dotnet build tools/VMXBench/VMXBench.csproj -c Release
```

Or directly from the VMXBench directory:

```bash
cd tools/VMXBench
dotnet build -c Release
```

## Running the Benchmark

### Basic Execution

```bash
dotnet run --project tools/VMXBench/VMXBench.csproj -c Release
```

Or from the VMXBench directory:

```bash
cd tools/VMXBench
dotnet run -c Release
```

### Expected Output Location

When run successfully, the benchmark will create:
- `results/vmxbench_results.csv` - Detailed results in CSV format
- `results/vmxbench_log.txt` - Human-readable benchmark log

## Test Configuration

The benchmark tests the following combinations:

### Frame Patterns (4 types)
1. **flat_gradient** - Luminance gradient from black to white across width
2. **checkerboard** - Alternating black and white blocks
3. **high_freq_noise** - Random high-frequency noise
4. **game_like** - Synthetic game-like content with HUD lines, color gradients, and pseudo-random foliage

### Pixel Formats (2 types)
- **UYVY** - 8-bit packed YUV format
- **P216** - Planar 16-bit YUV format

### Codec Profile
- **OMT_HQ** - High quality profile

### Quality Levels (4 values)
- `default` - Profile default quality
- `90` - Quality level 90
- `96` - Quality level 96
- `99` - Quality level 99 (highest)

### FrameMax Values (5 options)
- `default` - Encoder default
- `4 MB` - 4 megabytes
- `8 MB` - 8 megabytes
- `12 MB` - 12 megabytes
- `16 MB` - 16 megabytes

### MinQuality Values (5 options)
- `default` - Encoder default
- `90`
- `94`
- `96`
- `99`

### Total Tests
2 formats × 4 patterns × 4 qualities × 5 frameMax × 5 minQuality = **800 tests**

## Output Format

### CSV Columns

```
width,height,format,quality,frameMin,frameMax,minQuality,dcShift,encodedBytes,
estimatedMbpsAt60fps,encodeMilliseconds,success,error
```

**Field Descriptions:**
- `width` - Frame width (3840)
- `height` - Frame height (2160)
- `format` - Pixel format (UYVY or P216)
- `quality` - Quality level used
- `frameMin` - Minimum frame size in bytes
- `frameMax` - Maximum frame size in bytes
- `minQuality` - Minimum quality threshold
- `dcShift` - DC shift value
- `encodedBytes` - Actual encoded frame size in bytes
- `estimatedMbpsAt60fps` - Estimated bitrate at 60 FPS in Mbps
- `encodeMilliseconds` - Encoding time in milliseconds
- `success` - true/false indicating test success
- `error` - Error message if test failed

### Analysis Criteria

The benchmark helps answer key questions:

1. **Quality Scaling**: Does encodedBytes increase significantly from Quality 90 → 96 → 99?
   - If YES: Ultra10G hypothesis may be supported
   - If NO: Quality parameter has limited impact on output size

2. **FrameMax Impact**: Does encodedBytes grow from 4MB → 8MB → 16MB frameMax?
   - If YES: frameMax effectively constrains output size
   - If NO: frameMax may not meaningfully affect encoding

3. **Format Comparison**: Is P216 encoded output significantly larger than UYVY?
   - If YES: P216 may require different bandwidth planning
   - If NO: Format choice has minimal bandwidth impact

4. **Bitrate Sustainability**: Can Q99 + high frameMax achieve 4K60 at <1.5Gbps estimated bitrate?
   - If YES: Ultra10G bitrate targets appear feasible
   - If NO: Current parameters insufficient for stated goals

## Example Run Output

```
=== VMX Codec Benchmark ===
Target Resolution: 3840x2160
Target FPS: 60

Testing frame pattern: flat_gradient
  Completed 20 tests (18 successful)...
  Completed 40 tests (36 successful)...
  ...

=== Test Results Summary ===
Total tests: 800
Successful: 798

--- Results by Format ---
UYVY: avg=1.23MB, max=2.45MB, count=400
P216: avg=1.45MB, max=2.89MB, count=400

--- Quality Impact Analysis ---
Quality default: avg=1.20MB, count=200
Quality 90: avg=1.25MB, count=200
Quality 96: avg=1.35MB, count=200
Quality 99: avg=1.50MB, count=200
```

## Troubleshooting

### Error: "libvmx DLL not found"

**Solution**: Ensure libvmx.dll is available:
- Copy libvmx.dll to the working directory
- Or add its directory to the PATH environment variable
- On Windows: typically in C:\Program Files\obs-studio\obs-plugins\64bit\
- On macOS: typically in /Applications/OBS.app/Contents/FrameworksOr /usr/local/Frameworks/

### Error: "Invalid encoded length"

**Possible causes**:
- libvmx.dll is corrupted or incompatible
- Encoder initialization failed
- Output buffer too small (benchmarked with 20MB buffer)

### Error: "Encode failed"

**Possible causes**:
- Invalid frame data format
- Unsupported pixel format for the encoder version
- Encoder state corruption

## Performance Expectations

### Encoding Time
- Typically 50-200ms per 4K frame on modern hardware
- High quality levels may increase encoding time slightly
- Synthetic patterns may encode faster than real video content

### Memory Usage
- ~80MB per encoder instance (including 20MB output buffer)
- ~400MB total during full benchmark run

### Disk Space
- Results CSV: ~100-200 KB
- Results log: ~50-100 KB

## Integration with OMT Ultra10G

This benchmark is specifically designed for the OMT Ultra10G project to:
1. Verify that quality parameters produce expected bitrate scaling
2. Determine if P216 format provides bandwidth advantages
3. Validate that frameMax constraints are meaningful
4. Establish whether current parameters can support 4K60 @ 10Gbps

The tool intentionally:
- ✓ Measures real codec behavior, not theory
- ✓ Uses synthetic frames for reproducibility
- ✓ Reports actual encoded sizes, not estimates
- ✓ Tests encoder constraints in detail
- ✗ Does NOT depend on OBS
- ✗ Does NOT modify OBS files or settings
- ✗ Does NOT require OBS to be installed

## Implementation Notes

- Written in C# 7.3 compatible code for broad .NET Standard 2.0 support
- Uses P/Invoke to call native libvmx codec library
- Synthetic frame generation avoids video file I/O
- All timing measurements use Stopwatch for accuracy
- Results are not cached or estimated - every metric is measured

## Technical Details

### Synthetic Frame Generation

Each frame pattern is generated in UYVY format (4K = 16.6 MB per frame):

1. **flat_gradient**: Y component varies linearly across width (0-255), U/V constant
2. **checkerboard**: 32×32 pixel blocks alternate between black (Y=0) and white (Y=255)
3. **high_freq_noise**: Random byte values for all Y/U/V components
4. **game_like**: Base color + horizontal white lines + mid-section color gradients + bottom noise regions

### Encoding Pipeline

For each test:
1. Create OMTVMX1Codec instance with OMT_HQ profile
2. Set quality parameter (if not default)
3. Read current encoding parameters
4. Override frameMax and minQuality as specified in test
5. Encode frame using VMX_EncodeUYVY or VMX_EncodeP216
6. Read encoded bitstream with VMX_SaveTo
7. Record timing and size metrics
8. Destroy codec instance and clean up

## Future Enhancements

Potential areas for expansion:
- Support for additional pixel formats (YV12, NV12, etc.)
- Configurable test scenarios via command-line parameters
- Real video file input support
- Multi-threaded test execution
- Real-time bitrate calculation for streaming scenarios
- Integration with performance profiling tools

## License

This tool is part of the libomtnet project and follows the same MIT License.

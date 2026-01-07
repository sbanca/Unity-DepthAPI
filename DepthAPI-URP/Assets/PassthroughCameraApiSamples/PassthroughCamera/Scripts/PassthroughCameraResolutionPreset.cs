using UnityEngine;

namespace PassthroughCameraSamples
{
    // Preset list derived from supported resolutions logged by WebCamTextureManager on device.
    public enum PassthroughCameraResolutionPreset
    {
        [InspectorName("Custom (use RequestedResolution)")]
        Custom = 0,
        [InspectorName("Auto (max supported)")]
        Auto,
        [InspectorName("320x240")]
        R320x240,
        [InspectorName("640x360")]
        R640x360,
        [InspectorName("640x480")]
        R640x480,
        [InspectorName("720x480")]
        R720x480,
        [InspectorName("720x576")]
        R720x576,
        [InspectorName("800x600")]
        R800x600,
        [InspectorName("1024x576")]
        R1024x576,
        [InspectorName("1280x720")]
        R1280x720,
        [InspectorName("1280x960")]
        R1280x960,
        [InspectorName("1280x1080")]
        R1280x1080,
        [InspectorName("1280x1280")]
        R1280x1280,
    }

    public static class PassthroughCameraResolutionPresetExtensions
    {
        public static Vector2Int ToVector2Int(this PassthroughCameraResolutionPreset preset)
        {
            switch (preset)
            {
                case PassthroughCameraResolutionPreset.R320x240:
                    return new Vector2Int(320, 240);
                case PassthroughCameraResolutionPreset.R640x360:
                    return new Vector2Int(640, 360);
                case PassthroughCameraResolutionPreset.R640x480:
                    return new Vector2Int(640, 480);
                case PassthroughCameraResolutionPreset.R720x480:
                    return new Vector2Int(720, 480);
                case PassthroughCameraResolutionPreset.R720x576:
                    return new Vector2Int(720, 576);
                case PassthroughCameraResolutionPreset.R800x600:
                    return new Vector2Int(800, 600);
                case PassthroughCameraResolutionPreset.R1024x576:
                    return new Vector2Int(1024, 576);
                case PassthroughCameraResolutionPreset.R1280x720:
                    return new Vector2Int(1280, 720);
                case PassthroughCameraResolutionPreset.R1280x960:
                    return new Vector2Int(1280, 960);
                case PassthroughCameraResolutionPreset.R1280x1080:
                    return new Vector2Int(1280, 1080);
                case PassthroughCameraResolutionPreset.R1280x1280:
                    return new Vector2Int(1280, 1280);
                case PassthroughCameraResolutionPreset.Auto:
                case PassthroughCameraResolutionPreset.Custom:
                default:
                    return Vector2Int.zero;
            }
        }
    }
}

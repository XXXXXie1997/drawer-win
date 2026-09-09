param([string]$Source = (Join-Path $PSScriptRoot 'src/Drawer.Windows/Assets/DrawerIcon.png'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$drawerDestination = Join-Path $PSScriptRoot 'src/Drawer.Windows/Assets/DrawerIcon.ico'
$drawerSourceImage = [System.Drawing.Image]::FromFile($Source)
$drawerFrames = [System.Collections.Generic.List[byte[]]]::new()
$drawerSizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
try {
    foreach ($drawerSize in $drawerSizes) {
        $drawerBitmap = [System.Drawing.Bitmap]::new($drawerSize, $drawerSize, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $drawerGraphics = [System.Drawing.Graphics]::FromImage($drawerBitmap)
        $drawerAttributes = [System.Drawing.Imaging.ImageAttributes]::new()
        $drawerStream = [System.IO.MemoryStream]::new()
        try {
            $drawerGraphics.CompositingMode = [System.Drawing.Drawing2D.CompositingMode]::SourceCopy
            $drawerGraphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $drawerGraphics.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $drawerAttributes.SetWrapMode([System.Drawing.Drawing2D.WrapMode]::TileFlipXY)
            $drawerGraphics.DrawImage($drawerSourceImage, [System.Drawing.Rectangle]::new(0, 0, $drawerSize, $drawerSize), 0, 0, $drawerSourceImage.Width, $drawerSourceImage.Height, [System.Drawing.GraphicsUnit]::Pixel, $drawerAttributes)
            $drawerBitmap.Save($drawerStream, [System.Drawing.Imaging.ImageFormat]::Png)
            $drawerFrames.Add($drawerStream.ToArray())
        } finally { $drawerStream.Dispose(); $drawerAttributes.Dispose(); $drawerGraphics.Dispose(); $drawerBitmap.Dispose() }
    }
    $drawerOutput = [System.IO.File]::Create($drawerDestination)
    $drawerWriter = [System.IO.BinaryWriter]::new($drawerOutput)
    try {
        $drawerWriter.Write([uint16]0); $drawerWriter.Write([uint16]1); $drawerWriter.Write([uint16]$drawerSizes.Count)
        $drawerOffset = 6 + 16 * $drawerSizes.Count
        for ($drawerIndex = 0; $drawerIndex -lt $drawerSizes.Count; $drawerIndex++) {
            $drawerDimension = if ($drawerSizes[$drawerIndex] -eq 256) { 0 } else { $drawerSizes[$drawerIndex] }
            $drawerWriter.Write([byte]$drawerDimension); $drawerWriter.Write([byte]$drawerDimension)
            $drawerWriter.Write([byte]0); $drawerWriter.Write([byte]0)
            $drawerWriter.Write([uint16]1); $drawerWriter.Write([uint16]32)
            $drawerWriter.Write([uint32]$drawerFrames[$drawerIndex].Length); $drawerWriter.Write([uint32]$drawerOffset)
            $drawerOffset += $drawerFrames[$drawerIndex].Length
        }
        foreach ($drawerFrame in $drawerFrames) { $drawerWriter.Write($drawerFrame) }
    } finally { $drawerWriter.Dispose(); $drawerOutput.Dispose() }
} finally { $drawerSourceImage.Dispose() }
Write-Output $drawerDestination

Add-Type -AssemblyName System.Drawing

Add-Type -Name NativeIcon -Namespace AimDeciderTools -MemberDefinition @"
[System.Runtime.InteropServices.DllImport("user32.dll")]
public static extern bool DestroyIcon(System.IntPtr hIcon);
"@

function New-IconBitmap {
    param([int]$Size)

    $bmp = New-Object System.Drawing.Bitmap $Size, $Size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $bgColor = [System.Drawing.Color]::FromArgb(255, 0x0B, 0x0D, 0x12)
    $tealColor = [System.Drawing.Color]::FromArgb(255, 0x2E, 0xE6, 0xD6)
    $redColor = [System.Drawing.Color]::FromArgb(255, 0xFF, 0x5C, 0x66)

    $bgBrush = New-Object System.Drawing.SolidBrush $bgColor
    $g.FillEllipse($bgBrush, 0, 0, $Size, $Size)

    $ringWidth = [Math]::Max(1.5, $Size * 0.07)
    $tealPen = New-Object System.Drawing.Pen ($tealColor, $ringWidth)
    $margin = $Size * 0.16
    $g.DrawEllipse($tealPen, $margin, $margin, $Size - 2 * $margin, $Size - 2 * $margin)

    $center = $Size / 2.0
    $tickOuter = $margin - $ringWidth * 0.6
    $tickInner = $tickOuter - $Size * 0.16
    if ($tickInner -lt 0) { $tickInner = 0 }

    $g.DrawLine($tealPen, $center, $tickInner, $center, $tickOuter)
    $g.DrawLine($tealPen, $center, $Size - $tickOuter, $center, $Size - $tickInner)
    $g.DrawLine($tealPen, $tickInner, $center, $tickOuter, $center)
    $g.DrawLine($tealPen, $Size - $tickOuter, $center, $Size - $tickInner, $center)

    $dotRadius = [Math]::Max(1.5, $Size * 0.10)
    $redBrush = New-Object System.Drawing.SolidBrush $redColor
    $g.FillEllipse($redBrush, $center - $dotRadius, $center - $dotRadius, $dotRadius * 2, $dotRadius * 2)

    $g.Dispose()
    return $bmp
}

# Let Windows itself produce each single-resolution icon's image bytes (via
# GetHicon -> Icon.Save), instead of hand-rolling the DIB+mask encoding -
# guarantees the image data .NET's icon-to-resource embedder will accept.
function Get-SingleResolutionIcoBytes {
    param([System.Drawing.Bitmap]$Bitmap)

    $hIcon = $Bitmap.GetHicon()
    try {
        $icon = [System.Drawing.Icon]::FromHandle($hIcon)
        $ms = New-Object System.IO.MemoryStream
        $icon.Save($ms)
        return $ms.ToArray()
    }
    finally {
        [AimDeciderTools.NativeIcon]::DestroyIcon($hIcon) | Out-Null
    }
}

function Get-ImageEntryFromSingleIco {
    param([byte[]]$IcoBytes)

    # Single-entry ICO: ICONDIR(6) + one ICONDIRENTRY(16), entry describes the
    # image bytes that follow. Pull out just the entry header fields + raw
    # image bytes so they can be repackaged into a combined multi-entry ICO.
    $width = $IcoBytes[6]
    $height = $IcoBytes[7]
    $colorCount = $IcoBytes[8]
    $reserved = $IcoBytes[9]
    $planes = [BitConverter]::ToUInt16($IcoBytes, 10)
    $bitCount = [BitConverter]::ToUInt16($IcoBytes, 12)
    $bytesInRes = [BitConverter]::ToUInt32($IcoBytes, 14)
    $imageOffset = [BitConverter]::ToUInt32($IcoBytes, 18)

    $imageBytes = New-Object byte[] $bytesInRes
    [Array]::Copy($IcoBytes, $imageOffset, $imageBytes, 0, $bytesInRes)

    return [PSCustomObject]@{
        Width      = $width
        Height     = $height
        ColorCount = $colorCount
        Planes     = $planes
        BitCount   = $bitCount
        ImageBytes = $imageBytes
    }
}

$sizes = @(16, 32, 48, 256)
$entries = @()
foreach ($s in $sizes) {
    $bmp = New-IconBitmap -Size $s
    $singleIco = Get-SingleResolutionIcoBytes -Bitmap $bmp
    $entries += Get-ImageEntryFromSingleIco -IcoBytes $singleIco
    $bmp.Dispose()
}

# Preview PNG for visual review (not embedded in the .ico)
$previewBmp = New-IconBitmap -Size 256
$previewMs = New-Object System.IO.MemoryStream
$previewBmp.Save($previewMs, [System.Drawing.Imaging.ImageFormat]::Png)
[System.IO.File]::WriteAllBytes("$PSScriptRoot\icon-preview-256.png", $previewMs.ToArray())
$previewBmp.Dispose()

# Assemble the combined multi-resolution ICO
$icoPath = "$PSScriptRoot\..\src\AimDecider\AppIcon.ico"
$ms = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $ms

$writer.Write([UInt16]0)             # reserved
$writer.Write([UInt16]1)             # type = icon
$writer.Write([UInt16]$entries.Count)

$headerSize = 6 + (16 * $entries.Count)
$offset = $headerSize
foreach ($entry in $entries) {
    $writer.Write([Byte]$entry.Width)
    $writer.Write([Byte]$entry.Height)
    $writer.Write([Byte]$entry.ColorCount)
    $writer.Write([Byte]0)
    $writer.Write([UInt16]$entry.Planes)
    $writer.Write([UInt16]$entry.BitCount)
    $writer.Write([UInt32]$entry.ImageBytes.Length)
    $writer.Write([UInt32]$offset)
    $offset += $entry.ImageBytes.Length
}
foreach ($entry in $entries) {
    $writer.Write($entry.ImageBytes)
}

$writer.Flush()
[System.IO.File]::WriteAllBytes($icoPath, $ms.ToArray())
$writer.Dispose()
$ms.Dispose()

Write-Host "Wrote icon to $icoPath"
Write-Host "Preview PNG at $PSScriptRoot\icon-preview-256.png"

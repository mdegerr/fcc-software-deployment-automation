param(
    [Parameter(Mandatory = $true)]
    [string]$RepositoryRoot
)

Add-Type -AssemblyName System.Drawing

$sourceDirectory = Join-Path $RepositoryRoot 'docs\screenshots'
$outputDirectory = Join-Path $RepositoryRoot 'Kaynak Kod\SurumYakma_Agdan\SurumYakma\Assets\Help'
New-Item -ItemType Directory -Force -Path $outputDirectory | Out-Null

function Add-HelpMarker {
    param(
        [System.Drawing.Graphics]$Graphics,
        [int]$Number,
        [int]$StartX,
        [int]$StartY,
        [int]$EndX,
        [int]$EndY
    )

    $whitePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 255, 255, 255), 7)
    $bluePen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(255, 37, 99, 235), 4)
    $whitePen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $bluePen.EndCap = [System.Drawing.Drawing2D.LineCap]::ArrowAnchor
    $Graphics.DrawLine($whitePen, $StartX, $StartY, $EndX, $EndY)
    $Graphics.DrawLine($bluePen, $StartX, $StartY, $EndX, $EndY)

    $radius = 20
    $circle = New-Object System.Drawing.Rectangle(($StartX - $radius), ($StartY - $radius), ($radius * 2), ($radius * 2))
    $Graphics.FillEllipse([System.Drawing.Brushes]::White, $circle)
    $inner = New-Object System.Drawing.Rectangle(($StartX - $radius + 3), ($StartY - $radius + 3), (($radius - 3) * 2), (($radius - 3) * 2))
    $blueBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 37, 99, 235))
    $Graphics.FillEllipse($blueBrush, $inner)

    $font = New-Object System.Drawing.Font('Segoe UI', 13, [System.Drawing.FontStyle]::Bold)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $textRectangle = New-Object System.Drawing.RectangleF(
        [single]$inner.X,
        [single]$inner.Y,
        [single]$inner.Width,
        [single]$inner.Height)
    $Graphics.DrawString(
        [string]$Number,
        $font,
        [System.Drawing.Brushes]::White,
        $textRectangle,
        $format)

    $format.Dispose()
    $font.Dispose()
    $blueBrush.Dispose()
    $bluePen.Dispose()
    $whitePen.Dispose()
}

function New-AnnotatedHelpImage {
    param(
        [string]$SourceName,
        [string]$OutputName,
        [object[]]$Markers
    )

    $sourcePath = Join-Path $sourceDirectory $SourceName
    $outputPath = Join-Path $outputDirectory $OutputName
    $source = New-Object System.Drawing.Bitmap($sourcePath)
    $bitmap = New-Object System.Drawing.Bitmap($source.Width, $source.Height)
    $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
    $graphics.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $graphics.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $graphics.DrawImageUnscaled($source, 0, 0)
    foreach ($marker in $Markers) {
        Add-HelpMarker $graphics $marker[0] $marker[1] $marker[2] $marker[3] $marker[4]
    }
    $bitmap.Save($outputPath, [System.Drawing.Imaging.ImageFormat]::Png)
    $graphics.Dispose()
    $bitmap.Dispose()
    $source.Dispose()
}

New-AnnotatedHelpImage '01-main-installation-en.png' 'help-main.png' @(
    @(1, 18, 168, 45, 168),
    @(2, 18, 248, 45, 248),
    @(3, 18, 331, 45, 331),
    @(4, 18, 450, 45, 450),
    @(5, 1285, 185, 1245, 185),
    @(6, 1285, 252, 1245, 252),
    @(7, 1285, 425, 1245, 425)
)

New-AnnotatedHelpImage '02-ukb-settings-en.png' 'help-ukb-settings.png' @(
    @(1, 25, 102, 150, 102),
    @(2, 25, 134, 150, 134),
    @(3, 1070, 250, 1030, 250),
    @(4, 25, 364, 150, 364),
    @(5, 25, 427, 55, 427),
    @(6, 1070, 648, 1030, 648)
)

New-AnnotatedHelpImage '03-advanced-options-en.png' 'help-advanced-options.png' @(
    @(1, 25, 128, 205, 128),
    @(2, 25, 210, 55, 210),
    @(3, 1070, 330, 1030, 330),
    @(4, 25, 575, 55, 575),
    @(5, 1070, 648, 1030, 648)
)

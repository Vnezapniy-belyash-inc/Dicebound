Add-Type -AssemblyName System.Drawing
$root = $PSScriptRoot
[xml]$doc = Get-Content -LiteralPath (Join-Path $root 'gm-table-wireframe.svg') -Raw -Encoding utf8
$bmp = [System.Drawing.Bitmap]::new(1600,1180)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$g.SmoothingMode = 'AntiAlias'
$g.TextRenderingHint = 'AntiAliasGridFit'
function ColorOf($v) { [System.Drawing.ColorTranslator]::FromHtml($v) }
foreach ($n in $doc.DocumentElement.ChildNodes) {
    if ($n.LocalName -eq 'rect') {
        $b = [System.Drawing.SolidBrush]::new((ColorOf $n.fill))
        $p = [System.Drawing.Pen]::new((ColorOf $n.stroke),1)
        $g.FillRectangle($b,[float]$n.x,[float]$n.y,[float]$n.width,[float]$n.height)
        $g.DrawRectangle($p,[float]$n.x,[float]$n.y,[float]$n.width,[float]$n.height)
        $b.Dispose(); $p.Dispose()
    } elseif ($n.LocalName -eq 'text') {
        $style = [System.Drawing.FontStyle]::Regular
        if ([int]$n.GetAttribute('font-weight') -ge 600) {$style = [System.Drawing.FontStyle]::Bold}
        $sz = [float]$n.GetAttribute('font-size')
        $f = [System.Drawing.Font]::new('Segoe UI',$sz,$style,[System.Drawing.GraphicsUnit]::Pixel)
        $b = [System.Drawing.SolidBrush]::new((ColorOf $n.fill))
        $g.DrawString($n.InnerText,$f,$b,[float]$n.x,([float]$n.y-$sz*1.05))
        $f.Dispose(); $b.Dispose()
    } elseif ($n.LocalName -eq 'circle') {
        $r=[float]$n.r; $x=[float]$n.cx-$r; $y=[float]$n.cy-$r
        $p=[System.Drawing.Pen]::new((ColorOf $n.stroke),2)
        if($n.fill -ne 'none') {$b=[System.Drawing.SolidBrush]::new((ColorOf $n.fill));$g.FillEllipse($b,$x,$y,2*$r,2*$r);$b.Dispose()}
        $g.DrawEllipse($p,$x,$y,2*$r,2*$r);$p.Dispose()
    } elseif ($n.LocalName -eq 'path') {
        $p=[System.Drawing.Pen]::new((ColorOf $n.stroke),[float]$n.GetAttribute('stroke-width'))
        $commands=[regex]::Matches($n.d,'([MLHVZ])([0-9 .,-]*)')
        $px=0.0;$py=0.0;$sx=0.0;$sy=0.0
        foreach($c in $commands) {
            $v=@([regex]::Matches($c.Groups[2].Value,'[0-9.]+') | ForEach-Object {[float]$_.Value})
            switch($c.Groups[1].Value) {
                'M' {$px=$v[0];$py=$v[1];$sx=$px;$sy=$py}
                'H' {$g.DrawLine($p,[float]$px,[float]$py,[float]$v[0],[float]$py);$px=$v[0]}
                'V' {$g.DrawLine($p,[float]$px,[float]$py,[float]$px,[float]$v[0]);$py=$v[0]}
                'Z' {$g.DrawLine($p,[float]$px,[float]$py,[float]$sx,[float]$sy)}
            }
        }
        $p.Dispose()
    }
}
$bmp.Save((Join-Path $root 'gm-table-wireframe.png'),[System.Drawing.Imaging.ImageFormat]::Png)
$g.Dispose(); $bmp.Dispose()

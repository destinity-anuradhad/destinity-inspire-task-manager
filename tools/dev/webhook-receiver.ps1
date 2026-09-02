# Dev-only webhook sink: logs each incoming HTTP request (headers + body) to a file and returns 200.
# Uses a raw TcpListener so it needs no HTTP.sys URL ACL / admin rights.
$log = Join-Path $env:TEMP 'ttrecv.log'
Remove-Item $log -ErrorAction SilentlyContinue
$listener = [System.Net.Sockets.TcpListener]::new([System.Net.IPAddress]::Loopback, 8099)
$listener.Start()
while ($true) {
    $client = $listener.AcceptTcpClient()
    try {
        $stream = $client.GetStream()
        $ms = New-Object System.IO.MemoryStream
        $buffer = New-Object byte[] 4096
        do {
            $read = $stream.Read($buffer, 0, $buffer.Length)
            if ($read -gt 0) { $ms.Write($buffer, 0, $read) }
            Start-Sleep -Milliseconds 60
        } while ($stream.DataAvailable)
        $req = [System.Text.Encoding]::UTF8.GetString($ms.ToArray())
        Add-Content -Path $log -Value "=====REQUEST====="
        Add-Content -Path $log -Value $req
        $body = 'OK'
        $resp = "HTTP/1.1 200 OK`r`nContent-Type: text/plain`r`nContent-Length: $($body.Length)`r`nConnection: close`r`n`r`n$body"
        $rb = [System.Text.Encoding]::UTF8.GetBytes($resp)
        $stream.Write($rb, 0, $rb.Length)
        $stream.Flush()
    } catch { }
    finally { $client.Close() }
}

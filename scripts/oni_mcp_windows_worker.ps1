# Persistent Windows loopback HTTP worker for oni_mcp_bridge.py. Stdout is JSONL only.
$ErrorActionPreference = 'Stop'
[Console]::InputEncoding = [System.Text.UTF8Encoding]::new($false)
[Console]::OutputEncoding = [System.Text.UTF8Encoding]::new($false)
while ($null -ne ($line = [Console]::ReadLine())) {
    try {
        $inputMessage = $line | ConvertFrom-Json
        $uri = [Uri]$inputMessage.url
        if ($uri.Scheme -ne 'http' -or -not $uri.IsLoopback) {
            throw 'The ONI bridge accepts HTTP loopback endpoints only.'
        }
        $request = [System.Net.HttpWebRequest]::Create($uri)
        $request.Proxy = $null
        $request.AllowAutoRedirect = $false
        $request.Method = 'POST'
        $request.ContentType = 'application/json; charset=utf-8'
        $request.Accept = 'application/json, text/event-stream'
        $request.Timeout = [int]$inputMessage.timeoutMs
        $request.ReadWriteTimeout = [int]$inputMessage.timeoutMs
        foreach ($property in $inputMessage.headers.PSObject.Properties) {
            $request.Headers[$property.Name] = [string]$property.Value
        }
        $bytes = [Convert]::FromBase64String($inputMessage.body)
        $request.ContentLength = $bytes.Length
        $stream = $request.GetRequestStream()
        try { $stream.Write($bytes, 0, $bytes.Length) } finally { $stream.Dispose() }
        $response = $null
        try { $response = $request.GetResponse() }
        catch [System.Net.WebException] {
            if ($null -eq $_.Exception.Response) { throw }
            $response = $_.Exception.Response
        }
        try {
            $reader = [System.IO.StreamReader]::new($response.GetResponseStream(), [System.Text.Encoding]::UTF8)
            try { $body = $reader.ReadToEnd() } finally { $reader.Dispose() }
            $result = @{
                status = [int]$response.StatusCode
                session = $response.Headers['Mcp-Session-Id']
                body = $body
            }
        } finally { $response.Dispose() }
        $result | ConvertTo-Json -Compress -Depth 5
    } catch {
        # Avoid echoing a request, bearer token, or complete HTTP response into logs.
        @{ error = 'Windows loopback request failed. Check that ONI is running with OniMcp enabled.' } |
            ConvertTo-Json -Compress
    }
}

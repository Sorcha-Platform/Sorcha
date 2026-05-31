# SPDX-License-Identifier: MIT
# Copyright (c) 2026 Sorcha Contributors

BeforeAll {
    $lib = Join-Path $PSScriptRoot "../lib"
    . (Join-Path $lib "Common.ps1")
    . (Join-Path $lib "NodeInventory.ps1")

    function New-TempInventory([string]$json) {
        $p = Join-Path ([System.IO.Path]::GetTempPath()) ("inv-{0}.json" -f ([guid]::NewGuid()))
        $json | Set-Content -LiteralPath $p -Encoding UTF8
        return $p
    }

    $script:ValidJson = @'
{ "nodes": [
  { "id": "tiny", "role": "issuer", "gateway": "http://tiny:8090", "installationName": "tiny.sorcha.dev" },
  { "id": "n1", "role": "subscriber", "gateway": "https://n1.sorcha.dev", "installationName": "n1.sorcha.dev", "rendezvousCapable": true }
] }
'@
}

Describe "Get-DemoNodeInventory" {
    It "loads a valid inventory and returns all nodes" {
        $p = New-TempInventory $script:ValidJson
        $nodes = Get-DemoNodeInventory -Path $p
        $nodes.Count | Should -Be 2
        $nodes[0].id | Should -Be "tiny"
    }

    It "throws on a missing file" {
        { Get-DemoNodeInventory -Path "Z:\does\not\exist.json" } | Should -Throw "*not found*"
    }

    It "throws on duplicate ids" {
        $json = '{ "nodes": [ {"id":"a","role":"issuer","gateway":"http://a","installationName":"a"}, {"id":"a","role":"subscriber","gateway":"http://b","installationName":"b"} ] }'
        { Get-DemoNodeInventory -Path (New-TempInventory $json) } | Should -Throw "*duplicate node id*"
    }

    It "throws on a malformed gateway URL" {
        $json = '{ "nodes": [ {"id":"a","role":"issuer","gateway":"not a url","installationName":"a"} ] }'
        { Get-DemoNodeInventory -Path (New-TempInventory $json) } | Should -Throw "*malformed gateway*"
    }

    It "throws on a missing required field" {
        $json = '{ "nodes": [ {"id":"a","role":"issuer","gateway":"http://a"} ] }'
        { Get-DemoNodeInventory -Path (New-TempInventory $json) } | Should -Throw "*missing required field*installationName*"
    }

    It "throws on an invalid role" {
        $json = '{ "nodes": [ {"id":"a","role":"banana","gateway":"http://a","installationName":"a"} ] }'
        { Get-DemoNodeInventory -Path (New-TempInventory $json) } | Should -Throw "*invalid role*"
    }
}

Describe "Select-DemoNode / Get-DemoNodeByRole" {
    BeforeEach { $script:Nodes = Get-DemoNodeInventory -Path (New-TempInventory $script:ValidJson) }

    It "selects by id" {
        (Select-DemoNode -Inventory $script:Nodes -Id "n1").gateway | Should -Be "https://n1.sorcha.dev"
    }

    It "throws for an unknown id" {
        { Select-DemoNode -Inventory $script:Nodes -Id "nope" } | Should -Throw "*not found in inventory*"
    }

    It "selects the first node of a role" {
        (Get-DemoNodeByRole -Inventory $script:Nodes -Role 'subscriber').id | Should -Be "n1"
    }
}

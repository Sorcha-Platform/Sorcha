// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Azure VM deployment for Sorcha n1.sorcha.dev debug node
// Single VM running docker-compose with all services
// Estimated cost: ~$55/month (B2as_v2 + static IP + disk)

targetScope = 'subscription'

@description('Azure region for resources')
param location string = 'uksouth'

@description('Resource group name')
param resourceGroupName string = 'sorcha-n1'

@description('VM admin username')
param adminUsername string = 'sorcha'

@description('SSH public key for VM access')
@secure()
param sshPublicKey string

@description('VM size - B2as_v2 (2 vCPU, 8GB RAM, AMD) is cheapest for 8GB')
param vmSize string = 'Standard_B2as_v2'

@description('Your public IP for SSH access (CIDR notation, e.g. 203.0.113.50/32)')
param allowedSshCidr string

@description('Tags for all resources')
param tags object = {
  Environment: 'dev'
  Application: 'Sorcha'
  Node: 'n1'
  ManagedBy: 'Bicep'
}

// Resource Group
resource rg 'Microsoft.Resources/resourceGroups@2024-03-01' = {
  name: resourceGroupName
  location: location
  tags: tags
}

// Deploy all resources into the resource group
module vmResources 'n1-vm-resources.bicep' = {
  scope: rg
  name: 'n1-vm-resources'
  params: {
    location: location
    adminUsername: adminUsername
    sshPublicKey: sshPublicKey
    vmSize: vmSize
    allowedSshCidr: allowedSshCidr
    tags: tags
  }
}

// Outputs
output resourceGroupName string = rg.name
output vmPublicIp string = vmResources.outputs.publicIpAddress
output vmFqdn string = vmResources.outputs.vmFqdn
output sshCommand string = 'ssh ${adminUsername}@${vmResources.outputs.publicIpAddress}'
output sorchaUrl string = 'http://${vmResources.outputs.publicIpAddress}'

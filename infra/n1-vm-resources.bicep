// SPDX-License-Identifier: MIT
// Copyright (c) 2026 Sorcha Contributors
//
// Azure VM resources for Sorcha n1 debug node
// Creates: NSG, VNet, Public IP, NIC, VM with cloud-init

@description('Azure region')
param location string

@description('VM admin username')
param adminUsername string

@description('SSH public key')
@secure()
param sshPublicKey string

@description('VM size')
param vmSize string

@description('Allowed SSH CIDR')
param allowedSshCidr string

@description('Resource tags')
param tags object

// ============================================================================
// Network Security Group
// ============================================================================
resource nsg 'Microsoft.Network/networkSecurityGroups@2024-01-01' = {
  name: 'sorcha-n1-nsg'
  location: location
  tags: tags
  properties: {
    securityRules: [
      {
        name: 'AllowSSH'
        properties: {
          priority: 100
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: allowedSshCidr
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '22'
        }
      }
      {
        name: 'AllowHTTP'
        properties: {
          priority: 200
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '80'
        }
      }
      {
        name: 'AllowHTTPS'
        properties: {
          priority: 210
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '443'
        }
      }
      {
        name: 'AllowAspireDashboard'
        properties: {
          priority: 300
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: allowedSshCidr
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '18888'
        }
      }
      {
        name: 'AllowPeerGrpc'
        properties: {
          priority: 310
          direction: 'Inbound'
          access: 'Allow'
          protocol: 'Tcp'
          sourceAddressPrefix: '*'
          sourcePortRange: '*'
          destinationAddressPrefix: '*'
          destinationPortRange: '50051'
        }
      }
    ]
  }
}

// ============================================================================
// Virtual Network
// ============================================================================
resource vnet 'Microsoft.Network/virtualNetworks@2024-01-01' = {
  name: 'sorcha-n1-vnet'
  location: location
  tags: tags
  properties: {
    addressSpace: {
      addressPrefixes: [
        '10.0.0.0/16'
      ]
    }
    subnets: [
      {
        name: 'default'
        properties: {
          addressPrefix: '10.0.1.0/24'
          networkSecurityGroup: {
            id: nsg.id
          }
        }
      }
    ]
  }
}

// ============================================================================
// Public IP Address (Static for DNS)
// ============================================================================
resource publicIp 'Microsoft.Network/publicIPAddresses@2024-01-01' = {
  name: 'sorcha-n1-pip'
  location: location
  tags: tags
  sku: {
    name: 'Standard'
    tier: 'Regional'
  }
  properties: {
    publicIPAllocationMethod: 'Static'
    dnsSettings: {
      domainNameLabel: 'sorcha-n1'
    }
  }
}

// ============================================================================
// Network Interface
// ============================================================================
resource nic 'Microsoft.Network/networkInterfaces@2024-01-01' = {
  name: 'sorcha-n1-nic'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          publicIPAddress: {
            id: publicIp.id
          }
          subnet: {
            id: vnet.properties.subnets[0].id
          }
        }
      }
    ]
  }
}

// ============================================================================
// Cloud-init script (installs Docker + sets up Sorcha directory)
// ============================================================================
var cloudInitTemplate = '''
#cloud-config
package_update: true
package_upgrade: true

packages:
  - ca-certificates
  - curl
  - gnupg
  - lsb-release
  - jq
  - htop

runcmd:
  # Install Docker CE
  - install -m 0755 -d /etc/apt/keyrings
  - curl -fsSL https://download.docker.com/linux/ubuntu/gpg -o /etc/apt/keyrings/docker.asc
  - chmod a+r /etc/apt/keyrings/docker.asc
  - echo "deb [arch=amd64 signed-by=/etc/apt/keyrings/docker.asc] https://download.docker.com/linux/ubuntu noble stable" > /etc/apt/sources.list.d/docker.list
  - apt-get update
  - apt-get install -y docker-ce docker-ce-cli containerd.io docker-buildx-plugin docker-compose-plugin
  # Add admin user to docker group
  - usermod -aG docker __ADMIN_USER__
  # Create Sorcha working directory
  - mkdir -p /opt/sorcha
  - chown __ADMIN_USER__:__ADMIN_USER__ /opt/sorcha
  # Create data directories
  - mkdir -p /opt/sorcha/data
  - chown __ADMIN_USER__:__ADMIN_USER__ /opt/sorcha/data
  # Enable and start Docker
  - systemctl enable docker
  - systemctl start docker
  # Signal that cloud-init is done
  - touch /opt/sorcha/.cloud-init-complete

final_message: "Sorcha n1 VM ready"
'''
var cloudInit = replace(cloudInitTemplate, '__ADMIN_USER__', adminUsername)

// ============================================================================
// Virtual Machine
// ============================================================================
resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: 'sorcha-n1-vm'
  location: location
  tags: tags
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    osProfile: {
      computerName: 'sorcha-n1'
      adminUsername: adminUsername
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: sshPublicKey
            }
          ]
        }
      }
      customData: base64(cloudInit)
    }
    storageProfile: {
      imageReference: {
        publisher: 'Canonical'
        offer: 'ubuntu-24_04-lts'
        sku: 'server'
        version: 'latest'
      }
      osDisk: {
        name: 'sorcha-n1-osdisk'
        createOption: 'FromImage'
        diskSizeGB: 30
        managedDisk: {
          storageAccountType: 'Premium_LRS'
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
        }
      ]
    }
    diagnosticsProfile: {
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

// ============================================================================
// Auto-shutdown schedule (save costs - off overnight)
// ============================================================================
resource autoShutdown 'Microsoft.DevTestLab/schedules@2018-09-15' = {
  name: 'shutdown-computevm-${vm.name}'
  location: location
  tags: tags
  properties: {
    status: 'Enabled'
    taskType: 'ComputeVmShutdownTask'
    dailyRecurrence: {
      time: '2300' // 11 PM UTC (11 PM GMT)
    }
    timeZoneId: 'GMT Standard Time'
    targetResourceId: vm.id
    notificationSettings: {
      status: 'Disabled'
    }
  }
}

// ============================================================================
// Outputs
// ============================================================================
output publicIpAddress string = publicIp.properties.ipAddress
output vmFqdn string = publicIp.properties.dnsSettings.fqdn
output vmName string = vm.name
output vmId string = vm.id

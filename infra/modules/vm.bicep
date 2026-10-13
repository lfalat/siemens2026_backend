// The VM (ADR-001, ADR-015) with its system-assigned managed identity (iam.md §4). First-boot setup is
// infra/cloud-init.yaml, with the host scripts from deploy/host embedded into it.
//
// Azure does not allow custom data to change on an existing VM. A change to cloud-init or to the host scripts
// therefore needs a rebuilt VM (infrastructure-operations-architecture.md §4.3); redeploying with them unchanged
// is fine.

param location string
param name string
param tags object
param vmSize string
param image object
param imagePlan object
param subnetId string
param publicIpId string
param adminSshPublicKey string
param deploySshPublicKey string
param githubRepository string

@description('Admin user for the operator; scripts/azure/*.sh connect as this user over NetBird.')
param adminUsername string = 'azureops'

var cloudInit = reduce(
  [
    { key: '__DEPLOY_SSH_PUBLIC_KEY__', value: trim(deploySshPublicKey) }
    { key: '__GITHUB_REPOSITORY__', value: githubRepository }
    { key: '__HH_DEPLOY_B64__', value: loadFileAsBase64('../../deploy/host/hh-deploy') }
    { key: '__HH_DEPLOY_ENTRY_B64__', value: loadFileAsBase64('../../deploy/host/hh-deploy-entry') }
    { key: '__HH_PODMAN_SECRET_B64__', value: loadFileAsBase64('../../deploy/host/hh-podman-secret') }
    { key: '__HH_REPORT_OPENSCAP_B64__', value: loadFileAsBase64('../../deploy/host/hh-report-openscap') }
    { key: '__HH_REPORT_AIDE_B64__', value: loadFileAsBase64('../../deploy/host/hh-report-aide') }
    { key: '__HH_AUDIT_RULES_B64__', value: loadFileAsBase64('../../deploy/host/audit-cis.rules') }
  ],
  loadTextContent('../cloud-init.yaml'),
  (text, item) => replace(text, item.key, item.value)
)

resource nic 'Microsoft.Network/networkInterfaces@2024-05-01' = {
  name: 'nic-${name}'
  location: location
  tags: tags
  properties: {
    ipConfigurations: [
      {
        name: 'ipconfig1'
        properties: {
          privateIPAllocationMethod: 'Dynamic'
          subnet: {
            id: subnetId
          }
          publicIPAddress: {
            id: publicIpId
            properties: {
              deleteOption: 'Detach'
            }
          }
        }
      }
    ]
  }
}

resource vm 'Microsoft.Compute/virtualMachines@2024-07-01' = {
  name: name
  location: location
  tags: tags
  identity: {
    type: 'SystemAssigned'
  }
  plan: empty(imagePlan) ? null : imagePlan
  properties: {
    hardwareProfile: {
      vmSize: vmSize
    }
    storageProfile: {
      imageReference: {
        publisher: image.publisher
        offer: image.offer
        sku: image.sku
        version: 'latest'
      }
      osDisk: {
        name: 'osdisk-${name}'
        createOption: 'FromImage'
        deleteOption: 'Delete'
        diskSizeGB: 64
        managedDisk: {
          // Standard SSD qualifies for the 99.5 % single-VM SLA, enough for the 99 % target (ADD §6.2).
          storageAccountType: 'StandardSSD_LRS'
        }
      }
    }
    osProfile: {
      computerName: name
      adminUsername: adminUsername
      customData: base64(cloudInit)
      linuxConfiguration: {
        disablePasswordAuthentication: true
        ssh: {
          publicKeys: [
            {
              path: '/home/${adminUsername}/.ssh/authorized_keys'
              keyData: trim(adminSshPublicKey)
            }
          ]
        }
      }
    }
    networkProfile: {
      networkInterfaces: [
        {
          id: nic.id
          properties: {
            deleteOption: 'Delete'
          }
        }
      ]
    }
    diagnosticsProfile: {
      // Managed boot-diagnostics storage; Azure Serial Console needs it (ADR-014).
      bootDiagnostics: {
        enabled: true
      }
    }
  }
}

output name string = vm.name
output principalId string = vm.identity.principalId

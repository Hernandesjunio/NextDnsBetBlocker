param location string = resourceGroup().location
param acrName string
param imageTag string = 'latest'
param resourceGroupName string = resourceGroup().name
param logicAppName string = 'importer-scheduler'
param logicAppDisplayName string = 'DNS Blocker Importer - Weekly Scheduler'

// ============= VARIABLES =============
var acrLoginServer = '${acrName}.azurecr.io'
var imageName = 'importer'
var fullImageName = '${acrLoginServer}/${imageName}:${imageTag}'
var scheduleFrequency = 'Week'
var scheduleInterval = 1
var scheduleStartTime = '2025-02-16T00:00:00Z'
var containerGroupName = 'importer-run-weekly'
var containerCpu = 1
var containerMemory = 1

// ============= GET ACR REFERENCE =============
resource acr 'Microsoft.ContainerRegistry/registries@2023-07-01' existing = {
  name: acrName
}

// ============= LOGIC APPS CONNECTION (HTTP) =============
resource httpConnection 'Microsoft.Web/connections@2016-06-01' = {
  name: 'http-connection'
  location: location
  properties: {
    displayName: 'HTTP'
    api: {
      id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'http')
    }
  }
}

// ============= LOGIC APPS WORKFLOW =============
resource logicApp 'Microsoft.Logic/workflows@2019-05-01' = {
  name: logicAppName
  location: location
  properties: {
    definition: {
      '$schema': 'https://schema.management.azure.com/providers/Microsoft.Logic/schemas/2016-06-01/workflowdefinition.json#'
      contentVersion: '1.0.0.0'
      triggers: {
        Recurrence: {
          type: 'Recurrence'
          recurrence: {
            frequency: scheduleFrequency
            interval: scheduleInterval
            startTime: scheduleStartTime
            timeZone: 'UTC'
            schedule: {
              hours: [0]
              minutes: [0]
              weekDays: ['Sunday']
            }
          }
        }
      }
      actions: {
        DeleteOldContainer: {
          type: 'ApiConnection'
          inputs: {
            host: {
              connection: {
                name: '@parameters(\'$connections\')[\'http\'][\'connectionId\']'
              }
            }
            method: 'delete'
            uri: 'https://management.azure.com/subscriptions/@{subscription().subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.ContainerInstance/containerGroups/${containerGroupName}?api-version=2023-05-01'
            authentication: {
              type: 'ManagedServiceIdentity'
            }
          }
          runAfter: {}
          description: 'Delete old container instance if exists (cleanup)'
        }
        DelayAfterDelete: {
          type: 'Wait'
          inputs: {
            interval: {
              count: 5
              unit: 'Second'
            }
          }
          runAfter: {
            DeleteOldContainer: ['Succeeded', 'Failed']
          }
          description: 'Wait 5 seconds for deletion to complete'
        }
        CreateContainerInstance: {
          type: 'ApiConnection'
          inputs: {
            host: {
              connection: {
                name: '@parameters(\'$connections\')[\'http\'][\'connectionId\']'
              }
            }
            method: 'put'
            uri: 'https://management.azure.com/subscriptions/@{subscription().subscriptionId}/resourceGroups/${resourceGroupName}/providers/Microsoft.ContainerInstance/containerGroups/${containerGroupName}?api-version=2023-05-01'
            headers: {
              'Content-Type': 'application/json'
            }
            body: {
              location: location
              properties: {
                containers: [
                  {
                    name: 'importer'
                    properties: {
                      image: fullImageName
                      resources: {
                        requests: {
                          cpu: containerCpu
                          memoryInGb: containerMemory
                        }
                      }
                      environmentVariables: [
                        {
                          name: 'ASPNETCORE_ENVIRONMENT'
                          value: 'Production'
                        }
                        {
                          name: 'AzureStorageConnectionString'
                          secureValue: '@variables(\'StorageConnectionString\')'
                        }
                      ]
                    }
                  }
                ]
                osType: 'Linux'
                restartPolicy: 'Never'
                imageRegistryCredentials: [
                  {
                    server: acrLoginServer
                    username: listCredentials(acr.id, acr.apiVersion).username
                    password: listCredentials(acr.id, acr.apiVersion).passwords[0].value
                  }
                ]
              }
            }
            authentication: {
              type: 'ManagedServiceIdentity'
            }
          }
          runAfter: {
            DelayAfterDelete: ['Succeeded', 'Failed']
          }
          description: 'Create new container instance for weekly import'
        }
      }
      parameters: {
        '$connections': {
          defaultValue: {}
          type: 'Object'
        }
      }
    }
    parameters: {
      '$connections': {
        value: {
          http: {
            connectionId: httpConnection.id
            connectionName: 'http'
            id: subscriptionResourceId('Microsoft.Web/locations/managedApis', location, 'http')
          }
        }
      }
    }
  }
}

// ============= OUTPUTS =============
output logicAppName string = logicApp.name
output logicAppId string = logicApp.id
output logicAppUrl string = logicApp.properties.definition.triggers.Recurrence.recurrence.schedule.weekDays[0]
output containerGroupName string = containerGroupName
output imageName string = fullImageName

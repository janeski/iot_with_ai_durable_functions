@description('Azure region for the OpenAI resource')
param location string = resourceGroup().location

@description('Name of the Azure OpenAI account')
param openAiName string

@description('SKU for the Azure OpenAI account')
param skuName string = 'S0'

@description('GPT-4.1 model deployment capacity (in thousands of tokens per minute)')
param gpt41Capacity int = 30

@description('Embedding model deployment capacity (in thousands of tokens per minute)')
param embeddingCapacity int = 30

resource openAi 'Microsoft.CognitiveServices/accounts@2024-10-01' = {
  name: openAiName
  location: location
  kind: 'OpenAI'
  sku: {
    name: skuName
  }
  properties: {
    customSubDomainName: openAiName
    publicNetworkAccess: 'Enabled'
  }
}

resource gpt41Deployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'gpt-4.1'
  sku: {
    name: 'Standard'
    capacity: gpt41Capacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'gpt-4.1'
      version: '2025-04-14'
    }
  }
}

resource embeddingDeployment 'Microsoft.CognitiveServices/accounts/deployments@2024-10-01' = {
  parent: openAi
  name: 'text-embedding-3-small'
  sku: {
    name: 'GlobalStandard'
    capacity: embeddingCapacity
  }
  properties: {
    model: {
      format: 'OpenAI'
      name: 'text-embedding-3-small'
      version: '1'
    }
  }
  dependsOn: [gpt41Deployment]
}

@description('The endpoint of the Azure OpenAI account')
output endpoint string = openAi.properties.endpoint

@description('The name of the Azure OpenAI account')
output name string = openAi.name

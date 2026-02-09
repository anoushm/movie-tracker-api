using './main.bicep'

param location = 'westus2'
param containerAppName = 'movie-tracker-api'
param environmentName = 'movie-tracker-env'
param logAnalyticsName = 'movie-tracker-law'
param environmentType = 'demo'
param containerImage = 'movietracker.azurecr.io/movie-tracker-api:latest'
param containerCpu = '0.25'
param containerMemory = '0.5Gi'
param targetPort = 8080
param externalIngress = true
param minReplicas = 1
param maxReplicas = 3

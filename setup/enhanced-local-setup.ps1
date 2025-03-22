# Enhanced PowerShell script for setting up local development environment
# For Azure/Kubernetes test execution platform on Windows
# VERSION: Fixed with error handling and improved reliability

# Check if running as administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Host "Please run this script as Administrator" -ForegroundColor Red
    exit
}

# Configuration
$codeFolder = "D:\code\gitmain\TestExecutionPlatform\setup"
$kubeconfigPath = "$codeFolder\.kube\config"
$ErrorActionPreference = "Stop" # Make errors more visible

# Function to handle errors
function Handle-Error {
    param (
        [string]$Step,
        [string]$ErrorMessage
    )
    
    Write-Host "ERROR during $Step" -ForegroundColor Red
    Write-Host $ErrorMessage -ForegroundColor Red
    Write-Host "You may need to fix this issue and restart the script from this step." -ForegroundColor Yellow
}

# Create code folder if it doesn't exist
if (-not (Test-Path $codeFolder)) {
    try {
        New-Item -ItemType Directory -Path $codeFolder -Force | Out-Null
        Write-Host "Created code folder at $codeFolder" -ForegroundColor Green
    }
    catch {
        Handle-Error "creating code folder" $_.Exception.Message
    }
}

Write-Host "Setting up enhanced local development environment for test execution platform..." -ForegroundColor Cyan

# Step 1: Check for Docker and install if not present
Write-Host "Checking for Docker Desktop..." -ForegroundColor Yellow
$dockerPath = Get-Command docker -ErrorAction SilentlyContinue
if (-not $dockerPath) {
    Write-Host "Docker not found. Please install Docker Desktop for Windows from: https://www.docker.com/products/docker-desktop" -ForegroundColor Red
    Write-Host "After installing Docker Desktop, restart this script." -ForegroundColor Red
    exit
} else {
    Write-Host "Docker is installed." -ForegroundColor Green
    
    # Check if Docker is running
    try {
        $dockerStatus = docker info 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Host "Docker is not running. Please start Docker Desktop and run this script again." -ForegroundColor Red
            exit
        }
    }
    catch {
        Write-Host "Error checking Docker status. Please ensure Docker Desktop is running." -ForegroundColor Red
        exit
    }
}

# Step 2: Create a Docker network (ignore error if it already exists)
Write-Host "Creating Docker network..." -ForegroundColor Yellow
try {
    docker network inspect test-platform-network 2>&1 | Out-Null
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Docker network 'test-platform-network' already exists." -ForegroundColor Green
    } else {
        docker network create test-platform-network
        Write-Host "Docker network 'test-platform-network' created." -ForegroundColor Green
    }
}
catch {
    Handle-Error "creating Docker network" $_.Exception.Message
    # Continue anyway as this isn't critical
}

# Step 3: Start SQL Server container (stop and remove if already exists)
Write-Host "Starting SQL Server container..." -ForegroundColor Yellow
try {
    # Check if container exists
    $containerExists = docker ps -a --filter "name=sql-server" --format "{{.Names}}" | Select-String -Pattern "^sql-server$"
    if ($containerExists) {
        Write-Host "SQL Server container already exists. Removing it..." -ForegroundColor Yellow
        docker stop sql-server 2>&1 | Out-Null
        docker rm sql-server 2>&1 | Out-Null
    }
    
    # Start new container
    docker run -e "ACCEPT_EULA=Y" `
               -e "SA_PASSWORD=YourStrongPassword123!" `
               -p 1433:1433 `
               --name sql-server `
               --network test-platform-network `
               -d mcr.microsoft.com/mssql/server:2019-latest
    
    Write-Host "Waiting for SQL Server to initialize..." -ForegroundColor Yellow
    Start-Sleep -Seconds 20
    Write-Host "SQL Server container is running." -ForegroundColor Green
}
catch {
    Handle-Error "starting SQL Server container" $_.Exception.Message
}

# Step 4: Check for chocolatey and install if not present
Write-Host "Checking for Chocolatey..." -ForegroundColor Yellow
try {
    if (-not (Get-Command choco -ErrorAction SilentlyContinue)) {
        Write-Host "Installing Chocolatey package manager..." -ForegroundColor Yellow
        Set-ExecutionPolicy Bypass -Scope Process -Force
        [System.Net.ServicePointManager]::SecurityProtocol = [System.Net.ServicePointManager]::SecurityProtocol -bor 3072
        Invoke-Expression ((New-Object System.Net.WebClient).DownloadString('https://chocolatey.org/install.ps1'))
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "Chocolatey installed. Waiting for it to initialize..." -ForegroundColor Green
        Start-Sleep -Seconds 5
    } else {
        Write-Host "Chocolatey is already installed." -ForegroundColor Green
    }
}
catch {
    Handle-Error "installing Chocolatey" $_.Exception.Message
    Write-Host "Please install Chocolatey manually from https://chocolatey.org/install" -ForegroundColor Yellow
}

# Step 5: Install kubectl if not already installed
Write-Host "Installing kubectl..." -ForegroundColor Yellow
try {
    if (-not (Get-Command kubectl -ErrorAction SilentlyContinue)) {
        choco install kubernetes-cli -y
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "kubectl installed." -ForegroundColor Green
    } else {
        Write-Host "kubectl is already installed." -ForegroundColor Green
    }
}
catch {
    Handle-Error "installing kubectl" $_.Exception.Message
}

# Step 6: Install kind if not already installed
Write-Host "Installing kind (Kubernetes in Docker)..." -ForegroundColor Yellow
try {
    if (-not (Get-Command kind -ErrorAction SilentlyContinue)) {
        choco install kind -y
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "kind installed." -ForegroundColor Green
    } else {
        Write-Host "kind is already installed." -ForegroundColor Green
    }
}
catch {
    Handle-Error "installing kind" $_.Exception.Message
}

# Step 7: Set up local Kubernetes cluster
Write-Host "Setting up local Kubernetes environment..." -ForegroundColor Yellow
try {
    # Create a kind configuration file with local registry support
    $kindConfigPath = "$env:TEMP\kind-config.yaml"
@"
kind: Cluster
apiVersion: kind.x-k8s.io/v1alpha4
nodes:
- role: control-plane
  extraPortMappings:
  - containerPort: 80
    hostPort: 8080
    protocol: TCP
  - containerPort: 30000
    hostPort: 5000
    protocol: TCP
containerdConfigPatches:
- |-
  [plugins."io.containerd.grpc.v1.cri".registry.mirrors."localhost:5000"]
    endpoint = ["http://registry:5000"]
"@ | Out-File -FilePath $kindConfigPath -Encoding ascii

    # Check if cluster already exists
    $clusterExists = kind get clusters | Select-String -Pattern "^test-platform-cluster$"
    
    if ($clusterExists) {
        Write-Host "kind cluster 'test-platform-cluster' already exists. Recreating it..." -ForegroundColor Yellow
        kind delete cluster --name test-platform-cluster
        Start-Sleep -Seconds 5
    }
    
    # Create the kind cluster
    Write-Host "Creating local Kubernetes cluster with kind..." -ForegroundColor Yellow
    kind create cluster --name test-platform-cluster --config $kindConfigPath
    
    if ($LASTEXITCODE -ne 0) {
        throw "Failed to create kind cluster"
    }
    
    Write-Host "Local Kubernetes cluster created." -ForegroundColor Green
    
    # Copy the kubeconfig to the code folder
    Write-Host "Copying kubeconfig to $kubeconfigPath..." -ForegroundColor Yellow
    if (-not (Test-Path (Split-Path $kubeconfigPath -Parent))) {
        New-Item -ItemType Directory -Path (Split-Path $kubeconfigPath -Parent) -Force | Out-Null
    }
    Copy-Item "$env:USERPROFILE\.kube\config" -Destination $kubeconfigPath -Force
    Write-Host "Kubeconfig copied." -ForegroundColor Green
    
    # Configure kubectl context
    kubectl config use-context kind-test-platform-cluster
    Write-Host "kubectl configured to use the local cluster." -ForegroundColor Green
}
catch {
    Handle-Error "setting up Kubernetes" $_.Exception.Message
}

# Step 8: Set up a local container registry
Write-Host "Setting up local container registry..." -ForegroundColor Yellow
try {
    # Check if container exists
    $containerExists = docker ps -a --filter "name=registry" --format "{{.Names}}" | Select-String -Pattern "^registry$"
    if ($containerExists) {
        Write-Host "Container registry already exists. Removing it..." -ForegroundColor Yellow
        docker stop registry 2>&1 | Out-Null
        docker rm registry 2>&1 | Out-Null
    }
    
    docker run -d --name registry -p 5000:5000 --network test-platform-network registry:2
    Write-Host "Local container registry running at localhost:5000" -ForegroundColor Green

    # Connect the registry to the kind network
    docker network connect kind registry 2>&1 | Out-Null
    
    # Create registry service in Kubernetes
    $registryServiceYaml = "$env:TEMP\registry-service.yaml"
@"
apiVersion: v1
kind: Service
metadata:
  name: registry
  namespace: kube-system
spec:
  type: ClusterIP
  ports:
  - port: 5000
    targetPort: 5000
  selector:
    app: registry
"@ | Out-File -FilePath $registryServiceYaml -Encoding ascii

    kubectl apply -f $registryServiceYaml
    Write-Host "Connected registry to the kind network." -ForegroundColor Green
}
catch {
    Handle-Error "setting up local container registry" $_.Exception.Message
}

# Step 9: Set up Azure Functions development environment
Write-Host "Setting up Azure Functions development environment..." -ForegroundColor Yellow
try {
    # Check and install .NET SDK if needed
    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        Write-Host "Installing .NET SDK..." -ForegroundColor Yellow
        choco install dotnet-sdk -y
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host ".NET SDK installed." -ForegroundColor Green
    } else {
        Write-Host ".NET SDK is already installed." -ForegroundColor Green
    }

    # Check and install Azure Functions Core Tools if needed
    if (-not (Get-Command func -ErrorAction SilentlyContinue)) {
        Write-Host "Installing Azure Functions Core Tools..." -ForegroundColor Yellow
        # First install Node.js if needed
        if (-not (Get-Command node -ErrorAction SilentlyContinue)) {
            Write-Host "Installing Node.js..." -ForegroundColor Yellow
            choco install nodejs-lts -y
            # Refresh environment variables
            $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
            Write-Host "Node.js installed." -ForegroundColor Green
        }
        
        npm install -g azure-functions-core-tools@4 --unsafe-perm true
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "Azure Functions Core Tools installed." -ForegroundColor Green
    } else {
        Write-Host "Azure Functions Core Tools are already installed." -ForegroundColor Green
    }
}
catch {
    Handle-Error "setting up Azure Functions development environment" $_.Exception.Message
}

# Step 10: Start Azurite for Azure Storage Emulation
Write-Host "Starting Azurite (Azure Storage Emulator)..." -ForegroundColor Yellow
try {
    # Check if container exists
    $containerExists = docker ps -a --filter "name=azurite" --format "{{.Names}}" | Select-String -Pattern "^azurite$"
    if ($containerExists) {
        Write-Host "Azurite container already exists. Removing it..." -ForegroundColor Yellow
        docker stop azurite 2>&1 | Out-Null
        docker rm azurite 2>&1 | Out-Null
    }
    
    docker run -p 10000:10000 -p 10001:10001 -p 10002:10002 `
               --name azurite `
               --network test-platform-network `
               -d mcr.microsoft.com/azure-storage/azurite
    
    Write-Host "Azurite is running." -ForegroundColor Green
}
catch {
    Handle-Error "starting Azurite" $_.Exception.Message
}

# Step 11: Set up Kafka for local message queue
Write-Host "Setting up local Kafka instance..." -ForegroundColor Yellow
try {
    # Create a docker-compose file for Kafka
    $kafkaComposeFile = "$env:TEMP\kafka-compose.yml"
@"
version: '3'
services:
  zookeeper:
    image: confluentinc/cp-zookeeper:latest
    environment:
      ZOOKEEPER_CLIENT_PORT: 2181
    ports:
      - "2181:2181"
    networks:
      - test-platform-network
      
  kafka:
    image: confluentinc/cp-kafka:latest
    depends_on:
      - zookeeper
    ports:
      - "9092:9092"
    environment:
      KAFKA_BROKER_ID: 1
      KAFKA_ZOOKEEPER_CONNECT: zookeeper:2181
      KAFKA_ADVERTISED_LISTENERS: PLAINTEXT://kafka:29092,PLAINTEXT_HOST://localhost:9092
      KAFKA_LISTENER_SECURITY_PROTOCOL_MAP: PLAINTEXT:PLAINTEXT,PLAINTEXT_HOST:PLAINTEXT
      KAFKA_INTER_BROKER_LISTENER_NAME: PLAINTEXT
      KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR: 1
    networks:
      - test-platform-network

networks:
  test-platform-network:
    external: true
"@ | Out-File -FilePath $kafkaComposeFile -Encoding ascii

    # Install docker-compose if not already installed
    if (-not (Get-Command docker-compose -ErrorAction SilentlyContinue)) {
        Write-Host "Installing docker-compose..." -ForegroundColor Yellow
        choco install docker-compose -y
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "docker-compose installed." -ForegroundColor Green
    }

    # Check if containers are already running and stop them
    $kafkaRunning = docker ps --filter "name=kafka" --format "{{.Names}}" | Select-String -Pattern "kafka"
    $zookeeperRunning = docker ps --filter "name=zookeeper" --format "{{.Names}}" | Select-String -Pattern "zookeeper"
    
    if ($kafkaRunning -or $zookeeperRunning) {
        Write-Host "Kafka/Zookeeper containers already exist. Stopping them..." -ForegroundColor Yellow
        docker-compose -f $kafkaComposeFile down
    }

    # Start Kafka using docker-compose
    Write-Host "Starting Kafka and Zookeeper..." -ForegroundColor Yellow
    docker-compose -f $kafkaComposeFile up -d
    Write-Host "Kafka is running at localhost:9092" -ForegroundColor Green
}
catch {
    Handle-Error "setting up Kafka" $_.Exception.Message
}

# Step 12: Create a database in SQL Server
Write-Host "Creating TestExecutionDB database in SQL Server..." -ForegroundColor Yellow
try {
    # Install SQL Server command-line tools if not present
    if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
        Write-Host "Installing SQL Server command-line tools..." -ForegroundColor Yellow
        choco install sqlserver-cmdlineutils -y
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "SQL Server command-line tools installed." -ForegroundColor Green
    }

    # Create database script
    $createDbScript = @"
IF NOT EXISTS (SELECT * FROM sys.databases WHERE name = 'TestExecutionDB')
BEGIN
    CREATE DATABASE TestExecutionDB;
END
GO
USE TestExecutionDB;
GO
IF NOT EXISTS (SELECT * FROM sys.tables WHERE name = 'JobSchedule')
BEGIN
    CREATE TABLE JobSchedule (
        Id INT IDENTITY(1,1) PRIMARY KEY,
        JobName NVARCHAR(100) NOT NULL,
        ScheduleType NVARCHAR(50) NOT NULL,
        CronExpression NVARCHAR(100) NULL,
        IsEnabled BIT NOT NULL DEFAULT 1,
        CreatedDate DATETIME2 NOT NULL DEFAULT GETDATE(),
        LastRunDate DATETIME2 NULL
    );
END
GO
"@

    $sqlScriptPath = "$env:TEMP\create-db.sql"
    $createDbScript | Out-File -FilePath $sqlScriptPath -Encoding ascii

    # Execute the SQL script
    Write-Host "Running SQL script to create database and tables..." -ForegroundColor Yellow
    
    $attempts = 0
    $maxAttempts = 3
    $success = $false
    
    while (-not $success -and $attempts -lt $maxAttempts) {
        $attempts++
        try {
            sqlcmd -S localhost,1433 -U sa -P "YourStrongPassword123!" -i $sqlScriptPath -t 30
            if ($LASTEXITCODE -eq 0) {
                $success = $true
                Write-Host "TestExecutionDB database created successfully." -ForegroundColor Green
            } else {
                Write-Host "Attempt $attempts failed. Waiting before retrying..." -ForegroundColor Yellow
                Start-Sleep -Seconds 10
            }
        } catch {
            Write-Host "Attempt $attempts failed with error: $($_.Exception.Message)" -ForegroundColor Yellow
            Start-Sleep -Seconds 10
        }
    }
    
    if (-not $success) {
        Write-Host "Failed to create database after $maxAttempts attempts. You may need to create it manually." -ForegroundColor Red
        Write-Host "Use the following connection string: Server=localhost,1433;Database=TestExecutionDB;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=True;" -ForegroundColor Yellow
    }
}
catch {
    Handle-Error "creating database" $_.Exception.Message
    Write-Host "You may need to create the database manually." -ForegroundColor Yellow
}

# Step 13: Create the local.settings.json file
Write-Host "Creating local.settings.json file..." -ForegroundColor Yellow
try {
    $localSettingsJson = @"
{
  "IsEncrypted": false,
  "Values": {
    "AzureWebJobsStorage": "UseDevelopmentStorage=true",
    "FUNCTIONS_WORKER_RUNTIME": "dotnet",
    "KubernetesConfig:Provider": "Local",
    "KubernetesConfig:KubeConfigPath": "$($kubeconfigPath.Replace('\', '\\'))",
    "KubernetesConfig:ContainerRegistry": "localhost:5000",
    "SqlConnectionString": "Server=localhost,1433;Database=TestExecutionDB;User Id=sa;Password=YourStrongPassword123!;TrustServerCertificate=True;",
    "APPINSIGHTS_INSTRUMENTATIONKEY": "00000000-0000-0000-0000-000000000000",
    "Messaging:Provider": "Kafka",
    "Messaging:Kafka:BootstrapServers": "localhost:9092",
    "Messaging:Kafka:TestResultsTopic": "test-results-metadata",
    "Storage:ConnectionString": "UseDevelopmentStorage=true",
    "Storage:TestResultsContainer": "test-results",
    "Notifications:SendGrid:ApiKey": "LOCAL_DEVELOPMENT_NO_EMAILS",
    "Notifications:SendGrid:SenderEmail": "dev-alerts@localhost"
  },
  "Host": {
    "LocalHttpPort": 7071,
    "CORS": "*"
  }
}
"@

    $localSettingsPath = "$codeFolder\local.settings.json"
    $localSettingsJson | Out-File -FilePath $localSettingsPath -Encoding utf8
    Write-Host "local.settings.json created at: $localSettingsPath" -ForegroundColor Green
}
catch {
    Handle-Error "creating local.settings.json" $_.Exception.Message
}

# Step 14: Create test-results blob container in Azurite
Write-Host "Creating test-results container in Azurite..." -ForegroundColor Yellow
try {
    # Wait for Azurite to be fully running
    Write-Host "Waiting for Azurite to initialize..." -ForegroundColor Yellow
    Start-Sleep -Seconds 10
    
    # Install Azure CLI if needed for blob container creation
    if (-not (Get-Command az -ErrorAction SilentlyContinue)) {
        Write-Host "Installing Azure CLI..." -ForegroundColor Yellow
        choco install azure-cli -y
        # Refresh environment variables
        $env:Path = [System.Environment]::GetEnvironmentVariable("Path","Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path","User")
        Write-Host "Azure CLI installed." -ForegroundColor Green
    }
    
    # Create test-results container
    Write-Host "Creating 'test-results' container in Azurite..." -ForegroundColor Yellow
    $azuriteConnectionString = "DefaultEndpointsProtocol=http;AccountName=devstoreaccount1;AccountKey=Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==;BlobEndpoint=http://127.0.0.1:10000/devstoreaccount1;"
    
    # Try using az command
    az storage container create --name test-results --connection-string $azuriteConnectionString
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "Created 'test-results' container in Azurite." -ForegroundColor Green
    } else {
        Write-Host "Failed to create container with Azure CLI. The container will be created when the application first accesses it." -ForegroundColor Yellow
    }
}
catch {
    Handle-Error "creating test-results container" $_.Exception.Message
    Write-Host "The container will be created when the application first accesses it." -ForegroundColor Yellow
}

# Final summary
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host "Enhanced local development environment is ready!" -ForegroundColor Cyan
Write-Host "=======================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "Components available:" -ForegroundColor White
Write-Host "- SQL Server: localhost:1433 (sa/YourStrongPassword123!)" -ForegroundColor White
Write-Host "- Kubernetes: kubectl is configured to use the local cluster" -ForegroundColor White
Write-Host "- Local Container Registry: localhost:5000" -ForegroundColor White
Write-Host "- Azure Storage Emulator: Running on localhost:10000 (Blob), 10001 (Queue), 10002 (Table)" -ForegroundColor White
Write-Host "- Kafka: Running on localhost:9092" -ForegroundColor White
Write-Host ""
Write-Host "Configuration:" -ForegroundColor White
Write-Host "- local.settings.json: $localSettingsPath" -ForegroundColor White
Write-Host "- Kubernetes config: $kubeconfigPath" -ForegroundColor White
Write-Host ""
Write-Host "To start your Azure Functions project:" -ForegroundColor White
Write-Host "1. Navigate to your Function App directory" -ForegroundColor White
Write-Host "2. Copy the local.settings.json file from $localSettingsPath" -ForegroundColor White
Write-Host "3. Run: func start" -ForegroundColor White
Write-Host ""
Write-Host "To build and push images to local registry:" -ForegroundColor White
Write-Host "docker build -t localhost:5000/your-image:tag ." -ForegroundColor White
Write-Host "docker push localhost:5000/your-image:tag" -ForegroundColor White
Write-Host ""
Write-Host "Happy debugging!" -ForegroundColor Cyan
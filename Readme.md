# Test Execution Platform

This project implements a containerized test execution platform using Azure Functions and Kubernetes.

## Project Structure

- `src/TestExecutionPlatform.Functions`: Contains the Azure Functions that handle test job creation, result retrieval, and cleanup.
- `src/TestExecutionPlatform.Core`: Contains the core services for interacting with Kubernetes and managing test executions.
- `docker`: Contains Dockerfiles and scripts for building test runner images.

## Setup

1. Ensure you have the Azure Functions Core Tools installed.
2. Set up a Kubernetes cluster and obtain the kubeconfig file.
3. Update the `local.settings.json` file with your Kubernetes config path and container registry URL.
4. Build and publish the Docker images for the test runners.

## Usage

1. Use the `CreateAndRunTestJob` function to start a test execution.
2. Use the `GetTestResults` function to retrieve test results.
3. Use the `CleanupTestJob` function to manually clean up a test job, or let the automatic cleanup process handle it.

## Docker Images

The `docker` directory contains Dockerfiles for three types of test runners:
- DotNet Core
- Playwright C#
- Karate (Java)

Build and publish these images to your container registry before using the test execution platform.
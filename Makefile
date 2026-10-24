.DEFAULT_GOAL := help
.NOTPARALLEL:

COMPOSE_PROJECT_NAME ?= strangler-fig
APP_PORT ?= 5190
DB_VIEWER_PORT ?= 5191
LOGS_PORT ?= 5192
export COMPOSE_PROJECT_NAME APP_PORT DB_VIEWER_PORT LOGS_PORT

.PHONY: help setup telemetry-setup up down restart logs sample build test check format-check source-check security script-test mount-check
help setup telemetry-setup up down restart logs sample build test check format-check source-check security script-test mount-check:
	@./scripts/lab.sh $@

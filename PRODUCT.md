# Product

## Platform

web

## Stack

ASP.NET Core 10 Razor Pages, an extracted ASP.NET Core API, React with TypeScript, YARP and PostgreSQL. The existing and replacement page implementations coexist under one origin.

## Users

A requester prepares and submits a hiring requisition. Its designated approver reviews the request and accepts or rejects it.

## Product Purpose

Keep hiring requests and their decisions consistent while their application boundary changes. A requester can draft a title and reason, submit a request and inspect the resulting decision and history.

## Operating Context

Desktop and mobile browsers. Dashboard and personal notes remain server-rendered Razor Pages. Requisitions can be served by Razor Pages or React; after extraction both page implementations use the same API.

## Capabilities and Constraints

- A draft can be edited and submitted by its requester.
- Only the assigned approver can decide a submitted request; rejection requires a reason.
- Concurrent changes must produce an explicit conflict, preserving the recorded decision and audit history.
- Page routing is independent of data ownership. Returning to the Razor page does not restore a previous database writer.
- Authentication, participant authorization, antiforgery protection and recoverable errors apply across both interfaces.
- Notes stay private to their author. Normal application stop and restart preserve records.
- No external sending, candidate assessment, file upload, compensation or calendar integration is part of this application.

## Local Runtime

The current Razor Pages application owns requisitions, decisions and notes. The API and React topology in `docs/design.md` describes the planned extraction. Demonstration records are synthetic; no customer adoption or production deployment is claimed.

## Product Principles

- Make the current state and the next permitted action clear.
- Keep ownership and decisions consistent across both page implementations.
- Preserve user input when an operation fails.
- Keep navigation available when a dependency fails.

## Accessibility & Inclusion

Semantic controls, explicit labels, keyboard navigation, visible focus and accessible error and status messages across desktop and mobile.

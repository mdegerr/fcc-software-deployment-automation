# Security Policy

## Scope

This project controls Flight Control Computer (FCC) power, recovery state, serial communication, USB-NCM networking, and target software installation. Findings with security or safety implications must be reported privately to the repository owner without exposing field data in a public issue.

## Information that must not be shared

- real OFP or TEZI software packages,
- usernames, passwords, or device credentials,
- field `Settings.json` files,
- organization-specific IP, slot, or channel mappings,
- device serial numbers and identifiers,
- detailed field logs.

## Supported release

Security and field-reliability fixes are applied to the current stable release. Problems found in historical versions should first be reproduced with the current release where operationally safe.

## Safety principle

Do not start an installation with an unverified package, unknown channel mapping, or unauthorized target. Interrupting power during a critical installation stage may leave the target software unavailable.
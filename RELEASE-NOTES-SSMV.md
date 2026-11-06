# Release notes for RobustToolbox.

<!--
NOTE: automatically updated sometimes by version.py.
Don't change the format without looking at the script!
-->

<!--START TEMPLATE
## Master

### Breaking changes

*None yet*

### New features

*None yet*

### Bugfixes

*None yet*

### Other

*None yet*

### Internal

*None yet*


END TEMPLATE-->

## Master

### Breaking changes

*None yet*

### New features

*None yet*

### Bugfixes

*None yet*

### Other

*None yet*

### Internal

*None yet*


## mv-August2025-1.0.7

### Security

* Ports in upstream's fixes for denial of service bugs GHSA-m25p-v44q-gjqq & GHSA-p2x8-62mc-hwr6.  Some content-side changes are needed as bounds checking must be performed and they moved the location of NetSerializer, see their github security advisories for details.

## mv-August2025-1.0.6

### Breaking changes

* Fixes dotnet 10 breaking backwards compatability by backporting upstream's #6301.
  (This change is needed for a dotnet 10 launcher to use older RT/MV engines)


## mv-August2025-1.0.5

This should actually be 1.0.5 since it contains the following prior 1.0.5 changes:

* Data directory path now uses MV instead of 14
* Added legacy HWID generation for linux & mac


## mv-August2025-1.0.4

* Upstream Robust merge v266.0.0

## 1.0.4

### New features

* API provides additional information (engine type) to launcher (#3)


## 1.0.3

### Bugfixes

* MITM Mitigation (#2)


## 1.0.2

* Include upstream security fix (#5353)

## 1.0.1


## 1.0.0

### Breaking changes

* MV Key Auth replaces WizDen auth.

### New features

* MV Key Auth.
* VPN Block.
* SCSI disabled unless environment variable set.


## 0.0.6


## 0.0.6

* Updated to upstream 213.0.0 (includes the security fix for ImageSharp)

## 0.0.5

* Updated to upstream 189.0.0


## 0.0.4

### Internal

(Testing/ignore -- build pipeline test)


## 0.0.3

(Testing/ignore -- getting build pipeline set up)

## 0.0.2


## 0.0.1

### Internal

* SSMV Versioning


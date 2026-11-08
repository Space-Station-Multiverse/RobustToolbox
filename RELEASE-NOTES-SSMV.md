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

### Security

* Ports in upstream's fixes for denial of service bug GHSA-x84h-phgm-gq38

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


## mv-October2026-1.0.7

* Ports in upstream's fixes for denial of service bugs GHSA-m25p-v44q-gjqq & GHSA-p2x8-62mc-hwr6.  Some content-side changes are needed as bounds checking must be performed and they moved the location of NetSerializer, see their github security advisories for details.

## mv-October2026-1.0.6

* Upstream Robust merge v289.0.3


## mv-April2026-1.0.6

* Upstream Robust merge v275.2.0 (dotnet 10)

### Bugfixes

* Fix exception not being logged for 'Misc JWT Error'


## mv-November2025-1.0.5

* Upstream Robust merge v268.0.0

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


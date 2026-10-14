# Bundled country route rule sets

These binary sing-box rule sets are a pinned snapshot, copied into app-private storage with SHA-256 verification. VPN startup never downloads them. The sources classify destination IP ranges and curated domain lists; they do not guarantee that every resource of a country is included.

## Provenance and licenses

GeoIP: [SagerNet/sing-geoip](https://github.com/SagerNet/sing-geoip), rule-set commit `7fe82a879ad2666526730c195b55a6d8d9147908`. The builder imports `Dreamacro/maxmind-geoip`. GeoLite2 data is created by [MaxMind](https://www.maxmind.com/); its upstream [GeoLite2 terms](https://dev.maxmind.com/geoip/geolite2-free-geolocation-data/) apply to the source data. The SagerNet generator is GPL-3.0-or-later; its license is included as LICENSE-sing-geoip.txt. This generator license is not a substitute for the data terms.

GeoSite: [SagerNet/sing-geosite](https://github.com/SagerNet/sing-geosite), rule-set commit `9fa8691dd58baad5ba53575f192313c33ec81444`. Source domain lists: [v2fly/domain-list-community](https://github.com/v2fly/domain-list-community), MIT license. SagerNet generator: GPL-3.0-or-later, included as LICENSE-sing-geosite.txt.

Coverage: GeoIP for RU, CN, IR, DE, US, GB, KZ, BY, TR, UA; curated GeoSite for RU (`category-ru`), CN (`cn`), IR (`category-ir`). No country CIDR ranges or domain lists are authored by CehoProxy.

## Snapshot SHA-256

| File | Bytes | SHA-256 |
| --- | ---: | --- |
| geoip-ru.srs | 50107 | `6e23f5580dd443e2f9c4895adafee8d199c1a3487182ad5f0a2256cf59c7e53a` |
| geoip-cn.srs | 34185 | `ebee603fdf402314b44b9f653cdcf6d9cc9c41e84e2b3515e3123c5c920a93bc` |
| geoip-ir.srs | 12863 | `c88af3372f71234f6d015d0452ba472f26b0d9d62e82ae5167d066c1df24b8f5` |
| geoip-de.srs | 57276 | `e77bc006c95f9a6841c7f7979b541e396150d10875f2d3a414800a3c2f4da26b` |
| geoip-us.srs | 165388 | `98cee80d1377e625d43150e80d0b0c2b3dcc362d57d78099dbb89e3813152bca` |
| geoip-gb.srs | 49465 | `8e3c359a6857382cfa64bff0005a3ccd1ce5c9958906c73456418df713b22edd` |
| geoip-kz.srs | 4419 | `d9917a7657bc54c9d9072111041efe61d43f3439a2cbbdf7786a9a688d7bee2a` |
| geoip-by.srs | 1058 | `f05740a23ea705979fb2226036ae9cfa8d9d98f4f09f6accf483ad8ffb6acd72` |
| geoip-tr.srs | 13235 | `17f297424a30af0192fe5e842d4c8c91495ec92c434f9fff4ee65683d35304f1` |
| geoip-ua.srs | 17615 | `bfbbac5c2519d8ab005173d6073b62e74c226d8c7b334ba7b50a649d646ab3b7` |
| geosite-cn.srs | 56146 | `1d57439fb057886a6ad1c97386d4235737aa6290cbe7610b628489d5edb8b25e` |
| geosite-category-ir.srs | 1652 | `5e4ef5289e0b4f73018854730b2ab049860ec1e87e1a601fdbe976bcb6c669b6` |
| geosite-category-ru.srs | 8576 | `91cac7d9735f43d54d26102015f17c8a90cb740086e1bde290e2c03a4eb003a9` |

Exact immutable download URLs and source commit IDs are recorded in manifest.json. Update the whole pinned snapshot intentionally; do not replace files from a mutable branch during startup.

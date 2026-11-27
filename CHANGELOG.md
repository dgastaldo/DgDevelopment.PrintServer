# Changelog

## [1.8.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.7.0...server-v1.8.0) (2026-11-27)


### Features

* print the installer and update script messages in English or Italian ([#79](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/79)) ([391752e](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/391752e5ca6d00770abad6749b15a4c47e1e2f79))
* show the status page in English or Italian ([#78](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/78)) ([2be4080](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/2be40803e0958bc28306dbb4bb810e4628a02ebf))
* use English names for queue folders, option folders and job states ([#77](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/77)) ([4aba15c](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/4aba15c113fd30a345a62355532fcb940137e7d0))


### Bug Fixes

* resolve the remaining SonarCloud findings ([#87](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/87)) ([e426de3](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/e426de30d686666af6aa0c44ea8a3156a02c2b25))

## [1.7.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.6.0...server-v1.7.0) (2026-11-21)


### Features

* add the status web page with Identity login ([#75](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/75)) ([783ed0a](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/783ed0a22bd1c33a7c6e3c42f0fd90f98d2e9a1a))
* cancel, retry and query jobs, and submit files from code ([#74](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/74)) ([a87b5a3](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/a87b5a33070ddc4b60a7abc73f180b514ab42574))
* convert documents and images to PDF before printing ([#71](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/71)) ([eb02827](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/eb028270f07257553227c2c9304bacacd7716689))


### Bug Fixes

* resolve the remaining SonarCloud findings on develop ([#76](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/76)) ([d538b7b](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/d538b7b249b1ab510f528af03731bccc6d78d2a2))

## [1.6.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.5.0...server-v1.6.0) (2026-11-21)


### Features

* run as a Windows service, systemd unit and launchd daemon ([#69](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/69)) ([a3abd3b](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/a3abd3b71257dca913afb7cb9d2cc23733f05fd8))

## [1.5.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.4.0...server-v1.5.0) (2026-11-21)


### Features

* add the print worker ([#63](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/63)) ([a2caf3d](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/a2caf3daab04ff6875f580050e88f45617b10b4d))
* choose orientation and duplex from option subfolders ([#65](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/65)) ([119543d](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/119543db694d7de080c3d6d26247507218d02729))
* report printer problems from the CUPS, IPP and spooler backends ([#64](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/64)) ([1d9bcdb](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/1d9bcdb02143480253a7b0aac9460d81df610ec5))

## [1.4.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.3.0...server-v1.4.0) (2026-11-19)


### Features

* add the SQLite job queue ([#53](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/53)) ([fb51dae](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/fb51dae3acca74ed9951eefa60a9c58c19e0b52e))


### Bug Fixes

* simplify the watcher loops flagged by SonarCloud ([#51](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/51)) ([222b5a4](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/222b5a471bda3d28263c48b128ef03c14c2cd406))

## [1.3.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.2.2...server-v1.3.0) (2026-11-18)


### Features

* add the folder watcher ([#48](https://github.com/dgastaldo/DgDevelopment.PrintServer/issues/48)) ([61997c1](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/61997c1dca2e9a45bafceb416bf4c42cdcfe7d20))

## [1.2.2](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.2.1...server-v1.2.2) (2026-11-18)


### Bug Fixes

* address the remaining SonarCloud findings ([b7133a4](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/b7133a41fa0ef4f919df3b23959d224e9e68ad5f))
* address the remaining SonarCloud findings ([66cbaaa](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/66cbaaaec34ebd6d2314847014273229b9836280))
* configure the awaited RunAsync call to satisfy CA2007 ([31fa05a](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/31fa05a8d42468eb78f2d6b3e092e6299c44ff98))
* keep Kill void and document the swallowed exception instead of returning an unused bool ([b9e51d4](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/b9e51d4652558a567a0d7f8355e81c5c821b5f29))

## [1.2.1](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.2.0...server-v1.2.1) (2026-11-13)


### Bug Fixes

* open the develop to personal promotion PR instead of failing ([697ad40](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/697ad401d5132e02594b6fa6eb8e1f4b1df6e126))
* open the develop to personal promotion PR instead of failing ([1e1c594](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/1e1c594e9995ac743edeb41ea90eabc60577e815))

## [1.2.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.1.0...server-v1.2.0) (2026-11-13)


### Features

* add CUPS printer backend ([21e9e1c](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/21e9e1cbf9f6e006271b759c1ddd46da3ce95c2b))
* add IPP printer backend ([c59364d](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/c59364d535e6f91633cde791041fe4660c420d8a))
* add IPP printer backend ([ccd1f64](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/ccd1f648cd944e00b5aa7eeed660b21d6ec639f8))
* add printer backend abstraction and fake backend ([0f1e382](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/0f1e3820d4b286fd4efaf9a0be9240a9b1e24d60))
* add printer backend abstraction and fake backend ([4ead002](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/4ead00283d9daf7f34e08cfeae8648eeda9989fc))
* add Windows spooler printer backend ([deadac4](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/deadac45337d5ff9ba16ef0607872bffea421c55))


### Bug Fixes

* pin IPP server certificates instead of accepting any certificate ([4f6be05](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/4f6be051641c7a3a4353f42748082a711d7de212))

## [1.1.0](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.0.1...server-v1.1.0) (2026-11-13)


### Features

* one release per environment using the release-please version ([0b77fd2](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/0b77fd2f11d1716653a558bfbe22b0a46c0a17e8))
* one release per environment using the release-please version ([444de6c](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/444de6ca4d2fc98795d7d7022ba814fcf44c7ac9))

## [1.0.1](https://github.com/dgastaldo/DgDevelopment.PrintServer/compare/server-v1.0.0...server-v1.0.1) (2026-11-13)


### Bug Fixes

* address SonarCloud findings and renumber workflows ([514cd3c](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/514cd3c632ca536266f6319ef92abbe9f40ebce3))
* address SonarCloud findings in workflows and update script ([f5bed90](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/f5bed90ce014bd563acc3eb19079ed8382ea10f3))

## 1.0.0 (2026-11-13)


### Features

* scaffold solution ([8732641](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/8732641e5287a97cebc5bae056808e7bff3b6d5a))
* scaffold solution ([315b956](https://github.com/dgastaldo/DgDevelopment.PrintServer/commit/315b9567f97028deb153ebfd94e4caaac49d4700))

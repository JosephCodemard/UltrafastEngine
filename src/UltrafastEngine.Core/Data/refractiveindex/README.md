# Bundled refractiveindex.info pages

A small subset of the [refractiveindex.info database](https://github.com/polyanskiy/refractiveindex.info-database)
(public domain, CC0) in its YAML page format, embedded in `UltrafastEngine.Core`. Use a page as an index model:

```json
{ "index": { "kind": "refractiveindex", "page": "main/SiO2/Malitson" } }
```

For the full database clone it and set `ULTRAFAST_RII_PATH` to its `data` folder, or import single pages with
`ultrafast materials import <page.yml>`. See docs/reference/materials.md.

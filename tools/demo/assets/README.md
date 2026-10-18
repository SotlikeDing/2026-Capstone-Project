# Demo media assets

Put team-owned or appropriately licensed media here before running the full seed.
The seeder never downloads media and never invents licence metadata.

Expected optional layout:

```text
farms/<farm-key>/*.jpg       farm and lodge photographs
hunt/hunter/*.jpg            eight Johan photographs
hunt/ph/*.jpg                four Thabo photographs
hunt/vg/*.mp4                two clips, each no longer than 30 seconds
trophies/kudu.jpg            approved kudu trophy photograph
```

`manifest.json` records the expected files and licence/source notes. Populate each
entry's `source` before staging use. Run `check_demo.py --assets` to validate the
directory. The seeder uploads the JPGs and VG MP4 clips through MobileAPI, and asks
the recap worker to render the 20-25 second story ahead of the presentation.

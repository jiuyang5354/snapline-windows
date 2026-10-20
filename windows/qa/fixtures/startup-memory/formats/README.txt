These are generated test fixtures, not user screenshots.

format-00.png: RGBA PNG with semi-transparent pixels.
format-01.jpg: RGB JPEG.
format-02.bmp: RGB BMP.
format-03.gif: palette GIF.
format-04.tif: RGB TIFF.
format-05.png: grayscale PNG.
format-06.jpg: JPEG with generated sRGB ICC profile.
format-07.png: RGBA PNG with 144x192 DPI metadata.
format-08.tif: RGBA TIFF.

All are 151x89 pixels and have distinct generated color patterns.
expected-digests.txt records the decoded pixel hashes from the verified
published v1.4.7 binary, not the modified candidate implementation.
The fixtures were generated with Pillow; running the regression suite does
not require Pillow or Python.

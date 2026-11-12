import { inflateSync } from 'zlib';

import { PIN_BOX, PIN_GUARD_IMAGE, pinGuardImage, pinGuardUri } from './pinGuard';

/**
 * The guard image is the pin's tap target on iOS, so these tests are about geometry, not looks.
 *
 * `MKAnnotationView.image` resizes the view to the image and `AIRMapMarker.reactSetFrame:` takes its
 * size from `self.image`, so the bitmap's size *in points* — pixels divided by the screen scale it was
 * decoded at — becomes the annotation view's bounds, and those bounds are what MapKit hit-tests. A
 * one-pixel guard is therefore a pin nobody can tap: that was a shipped defect, and the first two
 * tests exist to make it impossible to reintroduce quietly.
 *
 * The rest is honesty about the payload. A data URI that fails to decode sets the flag `shouldUsePinView`
 * reads and still leaves every marker in the pool without a view, so the PNG is parsed rather than
 * string-matched, and its pixels are inflated rather than assumed transparent.
 */

const PNG_HEADER = 'data:image/png;base64,';

function pngOf(uri: string) {
  expect(uri.startsWith(PNG_HEADER)).toBe(true);
  return Buffer.from(uri.slice(PNG_HEADER.length), 'base64');
}

function headerOf(png: Buffer) {
  expect(png.subarray(1, 4).toString('ascii')).toBe('PNG');
  expect(png.subarray(12, 16).toString('ascii')).toBe('IHDR');
  return {
    width: png.readUInt32BE(16),
    height: png.readUInt32BE(20),
    bitDepth: png[24],
    colourType: png[25],
  };
}

/** Every pixel's four channels, filter bytes dropped, as one buffer. */
function pixelsOf(png: Buffer) {
  const idat = png.subarray(41, 41 + png.readUInt32BE(33));
  const raw = inflateSync(idat);
  const header = headerOf(png);
  const stride = header.width * 4 + 1;
  const pixels = Buffer.alloc(header.width * header.height * 4);
  for (let row = 0; row < header.height; row += 1) {
    raw.copy(pixels, row * header.width * 4, row * stride + 1, (row + 1) * stride);
  }
  return pixels;
}

describe('pinGuard - the bitmap is the tap target', () => {
  it.each([1, 2, 3])('is the pin box at %ix, in the pixels the loader decodes', (scale) => {
    const header = headerOf(pngOf(pinGuardUri(scale)));

    expect(header.width).toBe(PIN_BOX.width * scale);
    expect(header.height).toBe(PIN_BOX.height * scale);
    expect(header.bitDepth).toBe(8);
    expect(header.colourType).toBe(6); // RGBA, so the alpha bytes below are real
  });

  it.each([1, 2, 3])('is fully transparent at %ix, so no pixel of it shows', (scale) => {
    const pixels = pixelsOf(pngOf(pinGuardUri(scale)));

    expect(pixels.length).toBe(PIN_BOX.width * scale * PIN_BOX.height * scale * 4);
    expect(pixels.some((byte) => byte !== 0)).toBe(false);
  });

  it('picks the nearest bitmap that exists for a scale it was not built for', () => {
    expect(pinGuardUri(0.5)).toBe(pinGuardUri(1));
    expect(pinGuardUri(1.6)).toBe(pinGuardUri(2));
    expect(pinGuardUri(3.5)).toBe(pinGuardUri(3));
  });
});

describe('pinGuard - which platforms carry it', () => {
  it('gives iOS a decoded uri, because the view manager takes a string', () => {
    expect(pinGuardImage('ios', 3)).toEqual({ uri: pinGuardUri(3) });
  });

  it('gives Android nothing, because there it would only pad the snapshot', () => {
    expect(pinGuardImage('android', 3)).toBeUndefined();
  });

  it('resolves the image it hands the marker from the running platform', () => {
    expect(PIN_GUARD_IMAGE).toEqual(pinGuardImage());
  });
});

import { PixelRatio, Platform } from 'react-native';

/**
 * The box a pin occupies, in points: a 34pt disc, its stem, and the transparent padding that lifts
 * the tap target past the platform's 44pt minimum.
 *
 * It is a constant on purpose. MapKit sizes an annotation view from its `image` (see below), so the
 * box and the image have to agree; letting the box follow the content would make the hit target
 * change from 55 to 57 points the moment a pin is selected, for no gain.
 */
export const PIN_BOX = { width: 50, height: 57 } as const;

/**
 * What every marker is given as its `image`, and why its SIZE is load-bearing.
 *
 * The image first settles which view MapKit displays. `-[AIRMapMarker shouldUsePinView]`
 * (AIRMapMarker.m:321-323) answers `reactSubviews.count == 0 && !self.imageSrc`, and MapKit keeps the
 * first answer for the annotation's whole life. Under Fabric a marker is always childless when the map
 * registers it — `RCTMountingManager.mm:82-89` applies the child's props and finalizes it before the
 * parent mounts it — so `imageSrc` is the only term of that question JS can settle. A marker born
 * without it is Apple's red pin for as long as it lives, whatever it is handed later.
 *
 * The size is the half that decides whether a pin can be tapped at all. The interop layer hands the
 * paper `AIRMapMarker` its frame once, from the marker's own React layout
 * (`RCTViewComponentView.mm:561-562`) — that frame is the tap target. From then on the image owns the
 * bounds: an annotation view is sized to the image it is given, and `reactSetFrame:` would keep it
 * there anyway, because it takes `self.image.size` over the frame it is passed (AIRMapMarker.m:41-46).
 * Nothing puts the layout back afterwards: Fabric re-applies that frame only when the layout metrics
 * change, and the pool's never do — `useStationPins` mounts every marker already carrying its station.
 * So the shipped 1×1 guard left every pin with a third-of-a-point target at 3x while the pin itself
 * stayed visible, because a UIView does not clip its children: the pins looked right, and no tap ever
 * reached them. The bitmap is therefore exactly {@link PIN_BOX}.
 *
 * Pixels, not points. `setImageSrc:` decodes at `RCTScreenScale` (AIRMapMarker.m:331-339), so an
 * N-pixel bitmap is N/scale points tall — hence one PNG per screen scale, picked from `PixelRatio`.
 * Every pixel is alpha 0: the pin is the React subtree, and this is only the surface the platform
 * hit-tests.
 *
 * It is handed over as `{uri}` because that is the shape the library's type allows; `MapMarker.js:82-85`
 * unwraps it back to the string the view manager wants (`RCT_REMAP_VIEW_PROPERTY(image, imageSrc,
 * NSString)`), and an object reaching the native side would be dropped by `RCTConvert`.
 *
 * iOS only. On Android the same bitmap is composited *behind* the view snapshot at the bitmap's own
 * size (`MapMarker.java:getIcon`), so it has nothing to settle there — and a guard this large would
 * double the size of that composite at 2x and 3x.
 */
const PNG = 'data:image/png;base64,';

/**
 * 50×57, 100×114 and 150×171 — the box at 1x, 2x and 3x. Generated as fully transparent PNGs; the
 * first IHDR pair is the box, the rest is the same image at twice and three times the pixels.
 */
const GUARD_BY_SCALE: Readonly<Record<number, string>> = {
  1:
    PNG +
    'iVBORw0KGgoAAAANSUhEUgAAADIAAAA5CAYAAAB0+HhyAAAAIklEQVR42u3BMQEAAADCoPVPbQsvoAAAAAAAAAAAAAAATgYswQABraV6dQAAAABJRU5ErkJggg==',
  2:
    PNG +
    'iVBORw0KGgoAAAANSUhEUgAAAGQAAAByCAYAAAClbXTSAAAAQ0lEQVR42u3BMQEAAADCoPVPbQZ/oAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA+AyykgABS5Vy+QAAAABJRU5ErkJggg==',
  3:
    PNG +
    'iVBORw0KGgoAAAANSUhEUgAAAJYAAACrCAYAAACE5WWRAAAAeklEQVR42u3BMQEAAADCoPVPbQo/oAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOBikYIAASGXzmsAAAAASUVORK5CYII=',
};

/** The guard for a screen scale; anything outside 1-3 takes the nearest one that exists. */
export function pinGuardUri(scale: number = PixelRatio.get()): string {
  const step = Math.min(3, Math.max(1, Math.round(scale)));
  return GUARD_BY_SCALE[step];
}

/** What `Marker` is handed. `undefined` off iOS is deliberate — see the comment above. */
export function pinGuardImage(
  platform: string = Platform.OS,
  scale: number = PixelRatio.get(),
): { uri: string } | undefined {
  return platform === 'ios' ? { uri: pinGuardUri(scale) } : undefined;
}

/** Resolved once: the screen scale does not change while the app runs. */
export const PIN_GUARD_IMAGE = pinGuardImage();

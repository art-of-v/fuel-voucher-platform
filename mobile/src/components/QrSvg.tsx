import React, { useMemo } from 'react';
import { View, Dimensions, StyleProp, ViewStyle } from 'react-native';
import Svg, { Rect, G } from 'react-native-svg';
import QRCode from 'qrcode';

export interface QrSvgProps {
  /**
   * The data to encode. For a fuel voucher this is the brand-specific payload
   * string the pump scanner expects (OKKO / WOG / KLO / etc.).
   */
  value: string;
  /** Edge length of the rendered symbol, in points. Defaults to ~78% of screen width. */
  size?: number;
  /** Cell colour. Defaults to pure black for maximum scanner contrast. */
  fg?: string;
  /**
   * Cell background colour. Defaults to pure white — scanners require a
   * true-white quiet zone around the symbol regardless of the app theme.
   */
  bg?: string;
  /** Module quiet zone in QR-cell units. 2 is the qrcode lib default; 4 is the spec recommendation. */
  margin?: number;
  /** QR error-correction level. `M` is the spec default for vouchers. */
  errorCorrectionLevel?: 'L' | 'M' | 'Q' | 'H';
  style?: StyleProp<ViewStyle>;
}

/**
 * A locally-generated QR symbol, rendered as an SVG.
 *
 * Why local and not the server PNG: the previous flow downloaded the QR as a
 * `data:image/png;base64,…` blob from `/api/vouchers/my`. That made the
 * redemption screen dependent on a network round-trip, with no retry path
 * and no useful failure state — the user at the pump would see the static
 * "QR UNAVAILABLE" placeholder.
 *
 * Generating it here means:
 * - the QR is on screen in the same frame as the wallet data;
 * - a network failure makes the wallet not load, not the QR;
 * - the rendered symbol is fully under our control (size, quiet zone, colour).
 *
 * We honour `voucher.qrPayload` (the raw text the scanner reads) and fall back
 * to `voucher.qrCodeData`, then to `voucher.externalId` / `id`. The QR module
 * level, mask, version and encoding mode all live on the server because the
 * proprietary pump scanners are sensitive to those parameters — those remain
 * encoded by the backend, but the symbol is drawn here.
 */
export function QrSvg({
  value,
  size,
  fg = '#000000',
  bg = '#FFFFFF',
  margin = 2,
  errorCorrectionLevel = 'M',
  style,
}: QrSvgProps) {
  // A QR the cashier can scan needs to be big. Default to ≈78% of screen
  // width minus our standard sheet padding — large enough to scan from a
  // pump-side distance of 15–25 cm, small enough not to dominate the rest
  // of the redemption surface.
  const resolvedSize =
    size ?? Math.round(Dimensions.get('window').width * 0.78);
  const matrix = useMemo(() => {
    try {
      // The qrcode lib types don't expose `margin` on `QRCodeOptions`, but the
      // runtime accepts it (it's the quiet-zone width in modules). Cast through
      // a permissive shape to keep the call site readable.
      const opts = {
        errorCorrectionLevel,
        margin,
      } as unknown as Parameters<typeof QRCode.create>[1];
      return QRCode.create(value, opts).modules;
    } catch {
      return null;
    }
  }, [value, errorCorrectionLevel, margin]);

  if (!matrix || !matrix.size) {
    return (
      <View
        accessibilityRole="image"
        accessibilityLabel="QR code unavailable"
        style={[
          { width: resolvedSize, height: resolvedSize, backgroundColor: bg, borderRadius: 8 },
          style,
        ]}
      />
    );
  }

  const n = matrix.size;
  const cell = resolvedSize / n;

  const rects: React.ReactNode[] = [];
  for (let r = 0; r < n; r++) {
    for (let c = 0; c < n; c++) {
      if (matrix.data[r * n + c]) {
        rects.push(
          <Rect
            key={`${r}-${c}`}
            x={c * cell}
            y={r * cell}
            width={cell}
            height={cell}
            fill={fg}
          />,
        );
      }
    }
  }

  return (
    <View
      accessibilityRole="image"
      accessibilityLabel="Voucher QR code"
      style={[
        {
          width: resolvedSize,
          height: resolvedSize,
          backgroundColor: bg,
          padding: 0,
        },
        style,
      ]}
    >
      <Svg width={resolvedSize} height={resolvedSize} viewBox={`0 0 ${resolvedSize} ${resolvedSize}`}>
        <G>{rects}</G>
      </Svg>
    </View>
  );
}
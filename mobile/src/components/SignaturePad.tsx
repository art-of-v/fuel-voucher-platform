import React, { useState, useRef, useMemo } from 'react';
import { View, StyleSheet, LayoutRectangle, Pressable, Text } from 'react-native';
import { GestureDetector, Gesture, GestureHandlerRootView } from 'react-native-gesture-handler';
import Svg, { Path } from 'react-native-svg';
import { useDesignTokens } from '../core/hooks/useTheme';
import { RotateCcw } from 'lucide-react-native';

interface Props {
  onCapture: (signatureBase64: string) => void;
  height?: number;
}

export const SignaturePad: React.FC<Props> = ({ onCapture, height = 250 }) => {
  const tokens = useDesignTokens();
  const [paths, setPaths] = useState<string[]>([]);
  const [currentPath, setCurrentPath] = useState<string>('');
  const layout = useRef<LayoutRectangle | null>(null);

  const panGesture = useMemo(() => 
    Gesture.Pan()
      .onStart((e) => {
        const newPath = `M${e.x},${e.y}`;
        setCurrentPath(newPath);
      })
      .onUpdate((e) => {
        setCurrentPath((prev) => `${prev} L${e.x},${e.y}`);
      })
      .onEnd(() => {
        if (currentPath) {
          setPaths((prev) => [...prev, currentPath]);
        }
        setCurrentPath('');
      })
      .runOnJS(true),
    [currentPath]
  );

  const isInitialMount = React.useRef(true);
  React.useEffect(() => {
    if (isInitialMount.current) {
        isInitialMount.current = false;
        return;
    }
    
    if (paths.length > 0) {
      onCapture(JSON.stringify(paths));
    } else {
      onCapture('');
    }
  }, [paths, onCapture]);

  const clear = () => {
    setPaths([]);
    setCurrentPath('');
  };

  const allPaths = useMemo(() => {
    return [...paths, currentPath].filter(Boolean);
  }, [paths, currentPath]);

  return (
    <GestureHandlerRootView style={styles.container}>
      <View 
        style={[styles.pad, { height, borderColor: tokens.colors.borderLight, backgroundColor: tokens.colors.background }]}
        onLayout={(e) => (layout.current = e.nativeEvent.layout)}
      >
        <GestureDetector gesture={panGesture}>
          <View style={StyleSheet.absoluteFill}>
            <Svg style={StyleSheet.absoluteFill}>
              {allPaths.map((path, index) => (
                <Path
                  key={index}
                  d={path}
                  stroke={tokens.colors.primary}
                  strokeWidth={3}
                  fill="none"
                  strokeLinecap="round"
                  strokeLinejoin="round"
                />
              ))}
            </Svg>
          </View>
        </GestureDetector>

        <Pressable 
          onPress={clear}
          style={[styles.clearBtn, { backgroundColor: `${tokens.colors.primary}22` }]}
        >
          <RotateCcw size={16} color={tokens.colors.primary} />
          <Text style={[styles.clearText, { color: tokens.colors.primary }]}>ОЧИСТИТИ</Text>
        </Pressable>
      </View>
    </GestureHandlerRootView>
  );
};

const styles = StyleSheet.create({
  container: {
    width: '100%',
  },
  pad: {
    width: '100%',
    borderWidth: 1,
    borderRadius: 8,
    overflow: 'hidden',
    position: 'relative',
  },
  clearBtn: {
    position: 'absolute',
    bottom: 12,
    right: 12,
    flexDirection: 'row',
    alignItems: 'center',
    paddingHorizontal: 12,
    paddingVertical: 6,
    borderRadius: 20,
    gap: 6,
  },
  clearText: {
    fontSize: 10,
    fontFamily: 'Inter-Black',
  }
});

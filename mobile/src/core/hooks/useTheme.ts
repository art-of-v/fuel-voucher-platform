import { useMemo } from 'react';
import { useStore } from '../state/appStore';
import { getTokens, DesignTokens } from '../design/tokens';

export function useDesignTokens(): DesignTokens {
  const theme = useStore(state => state.theme);
  return useMemo(() => getTokens(theme), [theme]);
}

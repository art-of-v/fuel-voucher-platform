import React, { Component, ErrorInfo, ReactNode } from 'react';
import { View } from 'react-native';
import { useDesignTokens } from '../hooks/useTheme';
import { ErrorState } from './ErrorState';

interface ErrorBoundaryProps {
  children: ReactNode;
  fallback?: ReactNode;
}

interface ErrorBoundaryState {
  hasError: boolean;
  error: Error | null;
}

/**
 * A hook-consuming fallback, so the boundary itself can stay a class component.
 *
 * Before Phase 2 the fallback was hardcoded: `#000` canvas, `#FF4B4B` title,
 * `#00FF6A` button and — the real problem — the full stack trace rendered to
 * whoever hit the crash, including production users. `ErrorState` gates `detail`
 * behind `__DEV__`, so developers keep the trace and users get a sentence and a
 * button.
 */
function CrashFallback({ error, onRetry }: { error: Error | null; onRetry: () => void }) {
  const tokens = useDesignTokens();

  return (
    <View style={{ flex: 1, backgroundColor: tokens.colors.background }}>
      <ErrorState
        fullScreen
        onRetry={onRetry}
        detail={
          error ? [error.message, error.stack].filter(Boolean).join('\n\n') : undefined
        }
      />
    </View>
  );
}

export class ErrorBoundary extends Component<ErrorBoundaryProps, ErrorBoundaryState> {
  constructor(props: ErrorBoundaryProps) {
    super(props);
    this.state = { hasError: false, error: null };
  }

  static getDerivedStateFromError(error: Error): ErrorBoundaryState {
    return { hasError: true, error };
  }

  componentDidCatch(error: Error, errorInfo: ErrorInfo) {
    console.error('ErrorBoundary caught:', error, errorInfo);
  }

  handleRetry = () => {
    this.setState({ hasError: false, error: null });
  };

  render() {
    if (this.state.hasError) {
      if (this.props.fallback) {
        return this.props.fallback;
      }
      return <CrashFallback error={this.state.error} onRetry={this.handleRetry} />;
    }

    return this.props.children;
  }
}

import React from 'react';
import { Linking, Platform, StyleSheet, Text, View } from 'react-native';
import { useDesignTokens } from '../../../core/hooks/useTheme';
import { useI18n } from '../../../core/i18n';
import { Haptics } from '../../../core/utils/haptics';
import { BottomSheet, InlineFeedback, ListItem, LoadingState } from '../../../core/ui';
import {
    openNavigation,
    probeNavigators,
    toPlatformOS,
    type NavigatorOption,
    type RouteTarget,
} from '../lib/navigation';

interface Props {
    target: RouteTarget;
    /** Named under the title so the route's destination is never in doubt. */
    destinationName: string;
    visible: boolean;
    onClose: () => void;
}

/**
 * The "build route" choice: every navigator this phone can actually navigate with — the installed
 * ones marked as such, and the web-backed ones still one tap away through the browser.
 *
 * Availability comes from a `Linking.canOpenURL` probe (see lib/navigation for why it can
 * under-report) that runs when the sheet opens. The hand-off to the navigator happens only on the
 * tap, so the probe is paid for at most once per choice and never on screen load.
 */
export function NavigatorPickerSheet({ target, destinationName, visible, onClose }: Props) {
    const tokens = useDesignTokens();
    const t = useI18n((s) => s.t);
    const [options, setOptions] = React.useState<NavigatorOption[] | null>(null);

    React.useEffect(() => {
        if (!visible) return;
        // Which navigators a device has does not depend on the destination, so the previous result
        // stays on screen for the frame or two a re-probe takes — no reset, no loading flash.
        let active = true;
        probeNavigators(toPlatformOS(Platform.OS), target, (url) => Linking.canOpenURL(url))
            .then((found) => {
                if (active) setOptions(found);
            })
            .catch(() => {
                // A probe that dies outright leaves no rows — the sheet says so instead of hanging.
                if (active) setOptions([]);
            });
        return () => {
            active = false;
        };
    }, [visible, target]);

    const pick = (option: NavigatorOption) => {
        Haptics.impactAsync(Haptics.ImpactFeedbackStyle.Light);
        onClose();
        void openNavigation(option, (url) => Linking.openURL(url));
    };

    return (
        <BottomSheet
            visible={visible}
            onClose={onClose}
            title={t('map.navigatorPicker')}
            subtitle={destinationName}
            maxHeightRatio={0.6}
            testID="navigator-picker"
        >
            {options == null ? (
                <LoadingState variant="inline" message={t('map.navigatorScanning')} />
            ) : options.length === 0 ? (
                <InlineFeedback kind="info" message={t('map.noNavigators')} />
            ) : (
                options.map((option, i) => (
                    <ListItem
                        key={option.definition.id}
                        title={option.definition.name}
                        subtitle={option.appUrl ? t('map.navigatorInstalled') : t('map.navigatorWeb')}
                        leading={
                            <View
                                style={[
                                    styles.monogram,
                                    {
                                        backgroundColor: tokens.colors.primarySubtle,
                                        borderColor: tokens.colors.primary,
                                    },
                                ]}
                            >
                                <Text style={[styles.monogramText, { color: tokens.colors.primary }]}>
                                    {option.definition.monogram}
                                </Text>
                            </View>
                        }
                        onPress={() => pick(option)}
                        divider={i < options.length - 1}
                    />
                ))
            )}
        </BottomSheet>
    );
}

const styles = StyleSheet.create({
    monogram: {
        width: 24,
        height: 24,
        borderRadius: 12,
        borderWidth: 1,
        alignItems: 'center',
        justifyContent: 'center',
    },
    monogramText: {
        fontFamily: 'Rajdhani-Bold',
        fontSize: 10,
    },
});
import React from 'react';
import { View, Text, TouchableOpacity, StyleSheet, Dimensions } from 'react-native';
import { User, Phone } from 'lucide-react-native';

const { width } = Dimensions.get('window');

export default function HomeScreen() {
    return (
        <View style={styles.container}>
            {/* Background blur effect */}
            <View style={styles.blurCircle} />

            {/* Main content */}
            <View style={styles.content}>
                {/* Icon */}
                <View style={styles.iconContainer}>
                    <User color="#00FF80" size={48} strokeWidth={2} />
                </View>

                {/* Title */}
                <Text style={styles.title}>ПОТРІБНА АВТОРИЗАЦІЯ</Text>

                {/* Subtitle */}
                <Text style={styles.subtitle}>
                    Увійдіть для доступу до профілю та історії покупок
                </Text>

                {/* Phone Auth Button */}
                <TouchableOpacity style={styles.button}>
                    <Phone color="#000000" size={24} strokeWidth={3} />
                    <Text style={styles.buttonText}>ВХІД ЗА ТЕЛЕФОНОМ</Text>
                </TouchableOpacity>

                {/* Footer text */}
                <Text style={styles.footerText}>SMS VERIFICATION REQUIRED</Text>
            </View>
        </View>
    );
}

const styles = StyleSheet.create({
    container: {
        flex: 1,
        backgroundColor: '#000000',
        alignItems: 'center',
        justifyContent: 'center',
        padding: 16,
    },
    blurCircle: {
        position: 'absolute',
        top: '33%',
        left: '50%',
        width: 256,
        height: 256,
        backgroundColor: 'rgba(0, 255, 128, 0.2)',
        borderRadius: 128,
        transform: [{ translateX: -128 }],
        // Note: blur is not directly supported in RN, this is a visual approximation
    },
    content: {
        position: 'relative',
        zIndex: 10,
        alignItems: 'center',
        maxWidth: 384,
        width: '100%',
    },
    iconContainer: {
        width: 96,
        height: 96,
        backgroundColor: 'rgba(0, 255, 128, 0.1)',
        borderWidth: 4,
        borderColor: 'rgba(0, 255, 128, 0.3)',
        alignItems: 'center',
        justifyContent: 'center',
        marginBottom: 32,
    },
    title: {
        fontSize: width < 640 ? 24 : 36,
        fontWeight: '900',
        color: '#FFFFFF',
        fontFamily: 'Rajdhani',
        textTransform: 'uppercase',
        marginBottom: 16,
        textAlign: 'center',
    },
    subtitle: {
        fontSize: width < 640 ? 12 : 14,
        color: '#9CA3AF',
        fontFamily: 'Inter',
        marginBottom: 32,
        textAlign: 'center',
        paddingHorizontal: 16,
    },
    button: {
        flexDirection: 'row',
        alignItems: 'center',
        gap: 12,
        backgroundColor: '#00FF80',
        paddingHorizontal: width < 640 ? 24 : 32,
        paddingVertical: width < 640 ? 12 : 16,
        shadowColor: '#00FF80',
        shadowOffset: { width: 0, height: 0 },
        shadowOpacity: 0.5,
        shadowRadius: 40,
        elevation: 20,
        marginBottom: 24,
    },
    buttonText: {
        fontSize: width < 640 ? 16 : 18,
        fontWeight: '900',
        color: '#000000',
        fontFamily: 'Rajdhani',
        textTransform: 'uppercase',
    },
    footerText: {
        fontSize: 10,
        color: '#4B5563',
        fontFamily: 'Inter',
        textTransform: 'uppercase',
        letterSpacing: 2,
    },
});

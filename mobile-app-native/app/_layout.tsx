import React from 'react';
import { View, Text } from 'react-native';

export default function RootLayout() {
    return (
        <View style={{ flex: 1, backgroundColor: 'red', alignItems: 'center', justifyContent: 'center' }}>
            <Text style={{ fontSize: 24, fontWeight: 'bold', color: 'white' }}>SANITY CHECK - ZERO DEPS</Text>
            <Text style={{ color: 'white', marginTop: 10 }}>If you see this, the bundle is working.</Text>
        </View>
    );
}

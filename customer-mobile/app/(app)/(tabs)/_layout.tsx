import React from 'react';
import { Tabs } from 'expo-router';
import { Ionicons } from '@expo/vector-icons';

type IoniconName = React.ComponentProps<typeof Ionicons>['name'];

interface TabConfig {
  name: string;
  title: string;
  icon: IoniconName;
  activeIcon: IoniconName;
}

const TABS: TabConfig[] = [
  { name: 'home',      title: 'Home',      icon: 'home-outline',      activeIcon: 'home' },
  { name: 'price-list', title: 'Services', icon: 'pricetag-outline',  activeIcon: 'pricetag' },
  { name: 'my-orders', title: 'Orders',    icon: 'receipt-outline',   activeIcon: 'receipt' },
  { name: 'profile',   title: 'Profile',   icon: 'person-outline',    activeIcon: 'person' },
];

export default function TabsLayout() {
  return (
    <Tabs
      screenOptions={{
        headerShown: false,
        tabBarActiveTintColor:   '#1D4ED8',
        tabBarInactiveTintColor: '#6B7280',
        tabBarStyle: {
          borderTopWidth: 1,
          borderTopColor: '#E5E7EB',
          paddingTop: 4,
          paddingBottom: 4,
          height: 64,
        },
        tabBarLabelStyle: { fontSize: 11, fontWeight: '500' },
      }}
    >
      {TABS.map((tab) => (
        <Tabs.Screen
          key={tab.name}
          name={tab.name}
          options={{
            title: tab.title,
            tabBarIcon: ({ color, focused }) => (
              <Ionicons
                name={focused ? tab.activeIcon : tab.icon}
                size={24}
                color={color}
              />
            ),
            tabBarAccessibilityLabel: tab.title,
          }}
        />
      ))}
    </Tabs>
  );
}

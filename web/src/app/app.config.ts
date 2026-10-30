import { registerLocaleData } from '@angular/common';
import { provideHttpClient, withFetch } from '@angular/common/http';
import localeEs from '@angular/common/locales/es';
import { ApplicationConfig, LOCALE_ID, provideBrowserGlobalErrorListeners } from '@angular/core';
import { provideRouter, withComponentInputBinding } from '@angular/router';
import { definePreset } from '@primeuix/themes';
import Aura from '@primeuix/themes/aura';
import { ConfirmationService, MessageService } from 'primeng/api';
import { providePrimeNG } from 'primeng/config';
import { routes } from './app.routes';

registerLocaleData(localeEs);

// Klavyra's identity on top of Aura: piano-felt green, warm ivory surfaces and green-black "ink" for dark areas.
const KlavyraPreset = definePreset(Aura, {
  semantic: {
    primary: {
      50: '#edf9f4', 100: '#d3f0e3', 200: '#aae0cb', 300: '#74cbad', 400: '#3fb08c', 500: '#1f9672',
      600: '#14795c', 700: '#10614b', 800: '#0f4e3d', 900: '#0d4033', 950: '#06241c',
    },
    colorScheme: {
      light: {
        surface: {
          0: '#fffefb', 50: '#f7f4ec', 100: '#efebe0', 200: '#e2ddcf', 300: '#cdc7b6', 400: '#a8a292',
          500: '#837e70', 600: '#655f53', 700: '#4b473d', 800: '#34312a', 900: '#22201b', 950: '#14130f',
        },
        primary: { color: '{primary.700}', hoverColor: '{primary.800}', activeColor: '{primary.900}' },
      },
      dark: {
        surface: {
          0: '#ffffff', 50: '#eef3f0', 100: '#d9e2dd', 200: '#b7c5be', 300: '#93a59c', 400: '#70857b',
          500: '#52675e', 600: '#3a4e45', 700: '#293b33', 800: '#1b2c25', 900: '#12211b', 950: '#0b1713',
        },
        primary: { color: '{primary.300}', contrastColor: '{primary.950}', hoverColor: '{primary.200}', activeColor: '{primary.100}' },
      },
    },
  },
});

export const appConfig: ApplicationConfig = {
  providers: [
    provideBrowserGlobalErrorListeners(),
    { provide: LOCALE_ID, useValue: 'es' },
    provideRouter(routes, withComponentInputBinding()),
    provideHttpClient(withFetch()),
    providePrimeNG({
      translation: {
        accept: 'Sí',
        reject: 'No',
        cancel: 'Cancelar',
        choose: 'Elegir',
        emptyMessage: 'Sin resultados',
        emptyFilterMessage: 'Sin resultados',
      },
      theme: {
        preset: KlavyraPreset,
        options: {
          // Applied per region: the sidebar and the player's transport are "ink" panels on an ivory page.
          darkModeSelector: '.app-dark',
          cssLayer: { name: 'primeng', order: 'theme, base, primeng, components, utilities' },
        },
      },
    }),
    MessageService,
    ConfirmationService,
  ],
};

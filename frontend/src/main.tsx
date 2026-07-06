import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
// Bundled variable fonts (ADR 0001: local assets only, no CDN)
import '@fontsource-variable/inter';
import '@fontsource-variable/jetbrains-mono';
import { App } from './app/App';
import './index.css';

createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <App />
  </StrictMode>,
);

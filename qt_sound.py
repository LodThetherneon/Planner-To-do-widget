# qt_sound.py
import os

# Optional: keep pygame-based sound, but don't force it (avoid "bugos" dependency).
# If pygame is present, use it; otherwise do nothing.
try:
    import pygame  # type: ignore
except Exception:
    pygame = None


def play_sound(path: str) -> None:
    try:
        if not path or not os.path.exists(path):
            return
        if pygame is None:
            return
        if not pygame.mixer.get_init():
            pygame.mixer.init()
        pygame.mixer.music.load(path)
        pygame.mixer.music.set_volume(0.6)
        pygame.mixer.music.play()
    except Exception:
        # Never crash the UI due to sound
        return

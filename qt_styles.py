# qt_styles.py
APP_QSS = """
/* Base */
QWidget {
    color: #E6E6E6;
    font-family: "Segoe UI";
    font-size: 12px;
}

/* Main card background */
#MainCard {
    background-color: #1B232A;
    border: 1px solid #2C3A45;
    border-radius: 12px;
}

/* Header */
#HeaderBar {
    background-color: rgba(0,0,0,0);
}

#TitleLabel {
    font-size: 14px;
    font-weight: 800;
    color: #EAEAEA;
}

QToolButton {
    border: 0px;
    border-radius: 8px;
    padding: 6px 8px;
    background: transparent;
    color: #CFCFCF;
}
QToolButton:hover {
    background: rgba(255,255,255,0.08);
}
QToolButton:pressed {
    background: rgba(255,255,255,0.12);
}

QToolButton#CloseBtn:hover {
    background: rgba(255, 72, 72, 0.25);
    color: #FFD0D0;
}

/* Scroll area */ 
QScrollArea {
    border: 0px;
    background: transparent;
}

QScrollBar:vertical {
    background: transparent;
    width: 16px;
    margin: 6px 2px 6px 2px;
}

QScrollBar::add-page:vertical,
QScrollBar::sub-page:vertical {
    background: none;
}

/* 
   Modern Grab/Grip a csúszka közepén
   Az image: url(data:...) egy apró, átlátszó SVG-t hív meg,
   ami önmagában tartalmazza a 3 lekerekített csíkot (8x12 pixel méretben).
*/
QScrollBar::handle:vertical {
    background-color: rgba(255, 255, 255, 0.12);
    border-radius: 6px;
    min-height: 40px;
    
    /* Halvány (40%-os) csíkok SVG-ben */
    image: url("data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iOCIgaGVpZ2h0PSIxMiIgdmlld0JveD0iMCAwIDggMTIiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3QgeD0iMCIgeT0iMCIgd2lkdGg9IjgiIGhlaWdodD0iMiIgZmlsbD0icmdiYSgyNTUsMjU1LDI1NSwwLjQpIiByeD0iMSIvPjxyZWN0IHg9IjAiIHk9IjUiIHdpZHRoPSI4IiBoZWlnaHQ9IjIiIGZpbGw9InJnYmEoMjU1LDI1NSwyNTUsMC40KSIgcng9IjEiLz48cmVjdCB4PSIwIiB5PSIxMCIgd2lkdGg9IjgiIGhlaWdodD0iMiIgZmlsbD0icmdiYSgyNTUsMjU1LDI1NSwwLjQpIiByeD0iMSIvPjwvc3ZnPg==");
}

QScrollBar::handle:vertical:hover {
    background-color: rgba(255, 255, 255, 0.18);
    
    /* Élesebb (90%-os) csíkok hover állapotban */
    image: url("data:image/svg+xml;base64,PHN2ZyB3aWR0aD0iOCIgaGVpZ2h0PSIxMiIgdmlld0JveD0iMCAwIDggMTIiIHhtbG5zPSJodHRwOi8vd3d3LnczLm9yZy8yMDAwL3N2ZyI+PHJlY3QgeD0iMCIgeT0iMCIgd2lkdGg9IjgiIGhlaWdodD0iMiIgZmlsbD0icmdiYSgyNTUsMjU1LDI1NSwwLjkpIiByeD0iMSIvPjxyZWN0IHg9IjAiIHk9IjUiIHdpZHRoPSI4IiBoZWlnaHQ9IjIiIGZpbGw9InJnYmEoMjU1LDI1NSwyNTUsMC45KSIgcng9IjEiLz48cmVjdCB4PSIwIiB5PSIxMCIgd2lkdGg9IjgiIGhlaWdodD0iMiIgZmlsbD0icmdiYSgyNTUsMjU1LDI1NSwwLjkpIiByeD0iMSIvPjwvc3ZnPg==");
}

QScrollBar::add-line:vertical,
QScrollBar::sub-line:vertical {
    height: 0px;
}

/* Inputs */
QLineEdit, QComboBox {
    background-color: #12191F;
    border: 1px solid #34424D;
    border-radius: 8px;
    padding: 8px 10px;
}
QLineEdit:focus, QComboBox:focus {
    border: 1px solid #4C89FF;
}

QPushButton {
    background-color: #2B3A46;
    border: 1px solid #3C4D5B;
    border-radius: 8px;
    padding: 8px 10px;
    font-weight: 600;
}
QPushButton:hover {
    background-color: #324454;
}
QPushButton:pressed {
    background-color: #2A3944;
}
QPushButton:disabled {
    background-color: #24303A;
    color: #8E99A3;
    border-color: #2C3A45;
}

/* Task card */
.TaskCard {
    background-color: #12191F;
    border: 1px solid #2B3A46;
    border-radius: 10px;
}
.TaskTitle {
    font-size: 13px;
    font-weight: 700;
    color: #EAEAEA;
}
.TaskMeta {
    font-size: 11px;
    color: #EAEAEA;
}
.TaskMetaDone {
    font-size: 11px;
    color: #7C8791;
}
"""

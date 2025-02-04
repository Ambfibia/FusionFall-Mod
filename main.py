import sys, os, lzma
from PyQt5 import QtWidgets, QtCore

# Класс для хранения параметров заголовка unity3d
class UnityHeader:
    def __init__(self, flag="UnityWeb"):
        # Возможные варианты метки: "UnityWeb" и "streamed"
        self.flag_file_og = "UnityWeb"
        self.flag_file_retro = "streamed"
        self.flag_file = flag if flag in (self.flag_file_og, self.flag_file_retro) else self.flag_file_og
        self.ver1 = 2
        self.ver2 = "fusion-2.x.x"  # ровно 12 символов
        self.ver3 = "2.5.4b5"       # ровно 7 символов
        self.file_size = 0         # общий размер файла (64 байта заголовка + сжатые данные)
        self.first_offset = 64     # смещение начала данных
        self.file_zip_size = 0     # размер сжатой части
        self.file_unzip_size = 513 # начальное значение, далее прибавляются размеры файлов
        self.last_offset = 64      # последний байт

    def int_to_big_endian(self, num):
        return num.to_bytes(4, byteorder='big')

    def int_from_big_endian(self, b):
        return int.from_bytes(b, byteorder='big')

# Функции сжатия/распаковки с использованием LZMA
def compress_data(data: bytes) -> bytes:
    return lzma.compress(data, format=lzma.FORMAT_ALONE)

def decompress_data(data: bytes) -> bytes:
    return lzma.decompress(data)

# Главное окно с интерфейсом на Qt5
class MainWindow(QtWidgets.QMainWindow):
    def __init__(self):
        super().__init__()
        self.setWindowTitle("Unity3D Packer/Unpacker")
        self.resize(600, 400)
        self.setup_ui()

    def setup_ui(self):
        central_widget = QtWidgets.QWidget()
        self.setCentralWidget(central_widget)
        layout = QtWidgets.QVBoxLayout(central_widget)

        # Панель выбора версии заголовка
        flag_layout = QtWidgets.QHBoxLayout()
        flag_label = QtWidgets.QLabel("Header Flag:")
        self.flag_combo = QtWidgets.QComboBox()
        self.flag_combo.addItems(["UnityWeb", "streamed"])
        flag_layout.addWidget(flag_label)
        flag_layout.addWidget(self.flag_combo)
        layout.addLayout(flag_layout)

        # Кнопки основных действий
        self.btn_pack = QtWidgets.QPushButton("Pack (.unity3d) [Compressed]")
        self.btn_unpack = QtWidgets.QPushButton("Extract Files")
        self.btn_pack_uncomp = QtWidgets.QPushButton("Pack (.unity3d-uncompress) [Uncompressed]")
        self.btn_extract_raw = QtWidgets.QPushButton("Extract Raw Header")

        layout.addWidget(self.btn_pack)
        layout.addWidget(self.btn_pack_uncomp)
        layout.addWidget(self.btn_unpack)
        layout.addWidget(self.btn_extract_raw)

        # Список для отображения имён файлов, выбранных для упаковки
        self.file_list_widget = QtWidgets.QListWidget()
        layout.addWidget(self.file_list_widget)

        # Привязка сигналов к слотам
        self.btn_pack.clicked.connect(lambda: self.pack_unity3d(compress=True))
        self.btn_pack_uncomp.clicked.connect(lambda: self.pack_unity3d(compress=False))
        self.btn_unpack.clicked.connect(self.extract_files)
        self.btn_extract_raw.clicked.connect(self.extract_raw_header)

    def pack_unity3d(self, compress=True):
        """
        Упаковка файлов из выбранной папки в единый .unity3d файл.
        Если compress=True – данные сжимаются LZMA, иначе – записываются «как есть».
        """
        # Выбор выходного файла
        options = QtWidgets.QFileDialog.Options()
        output_filename, _ = QtWidgets.QFileDialog.getSaveFileName(
            self,
            "Select output .unity3d file",
            "",
            "Unity web player (*.unity3d);;All Files (*)",
            options=options
        )
        if not output_filename:
            return

        # Выбор папки с файлами для упаковки
        folder_path = QtWidgets.QFileDialog.getExistingDirectory(self, "Select folder with files")
        if not folder_path:
            return

        # Задаём список ожидаемых файлов в нужном порядке
        expected_files = [
            "Assembly - CSharp - first pass.dll",
            "Assembly - CSharp.dll",
            "Assembly - UnityScript - first pass.dll",
            "Mono.Data.Tds.dll",
            "Mono.Security.dll",
            "System.Configuration.Install.dll",
            "System.Configuration.dll",
            "System.Data.dll",
            "System.Drawing.dll",
            "System.EnterpriseServices.dll",
            "System.Security.dll",
            "System.Transactions.dll",
            "System.Xml.dll",
            "System.dll",
            "mysql.data.dll",
            "mainData",
            "sharedassets0.assets"
        ]

        self.file_list_widget.clear()
        file_info_list = []
        for fname in expected_files:
            full_path = os.path.join(folder_path, fname)
            if not os.path.isfile(full_path):
                QtWidgets.QMessageBox.warning(self, "File Not Found", f"Expected file '{fname}' not found in folder.")
                return
            file_info_list.append((fname, full_path))
            self.file_list_widget.addItem(fname)

        # Создаём объект UnityHeader с выбранной версией (из комбобокса)
        selected_flag = self.flag_combo.currentText()
        header = UnityHeader(flag=selected_flag)

        # Подсчитываем суммарный размер данных – начальное значение 513 + сумма размеров файлов
        total_data_size = 0
        file_sizes = []
        for fname, fpath in file_info_list:
            size = os.path.getsize(fpath)
            file_sizes.append(size)
            total_data_size += size
        header.file_unzip_size = 513 + total_data_size

        # Выделяем массив headerzip нужного размера: (file_unzip_size + 2)
        headerzip = bytearray(header.file_unzip_size + 2)

        pos = 512  # с этого смещения будут записываться данные файлов
        pos2 = 4   # с этого смещения – метаданные (после 4 байт, где записано число файлов)

        # Записываем в headerzip количество файлов (4 байта, big-endian)
        num_files = len(expected_files)
        headerzip[0:4] = header.int_to_big_endian(num_files)

        # Для каждого файла записываем его имя и метаданные
        for i, (fname, fpath) in enumerate(file_info_list):
            # Имя файла (ASCII) с нулевым окончанием
            name_bytes = fname.encode('ascii')
            headerzip[pos2:pos2+len(name_bytes)] = name_bytes
            pos2 += len(name_bytes)
            headerzip[pos2] = 0  # нулевой байт
            pos2 += 1

            # Если файл "mainData" (индекс 15) – добавляем дополнительный байт (как в оригинале)
            if i == 15:
                headerzip[pos] = 1
                pos += 1

            # Записываем смещение (4 байта, big-endian) – где начинается запись файла
            headerzip[pos2:pos2+4] = header.int_to_big_endian(pos)
            pos2 += 4

            # Записываем размер файла (4 байта, big-endian)
            size = file_sizes[i]
            headerzip[pos2:pos2+4] = header.int_to_big_endian(size)
            pos2 += 4

            # Увеличиваем смещение для записи данных
            pos += size

        # Записываем данные файлов начиная с позиции 512
        pos_data = 512
        for i, (fname, fpath) in enumerate(file_info_list):
            with open(fpath, "rb") as f:
                file_data = f.read()
            headerzip[pos_data:pos_data+len(file_data)] = file_data
            pos_data += len(file_data)
            if i == 15:
                headerzip[pos_data] = 1
                pos_data += 1

        # Добавляем завершающие байты: сначала 1, затем 0
        headerzip[pos_data] = 1
        pos_data += 1
        headerzip[pos_data] = 0

        # Если выбран режим сжатия – сжимаем headerzip, иначе оставляем без изменений
        if compress:
            comp_data = compress_data(headerzip)
        else:
            comp_data = headerzip

        # Формируем 64-байтовый основной заголовок
        main_header = bytearray(64)
        # Записываем флаг (8 байт)
        flag_bytes = header.flag_file.encode('ascii')
        flag_bytes = flag_bytes.ljust(8, b'\x00')[:8]
        main_header[0:8] = flag_bytes
        # Следующие 4 байта оставляем нулевыми (как в оригинале)
        # Записываем ver1 (1 байт) по смещению 12
        main_header[12] = header.ver1
        # Записываем ver2 (12 байт) по смещению 13
        ver2_bytes = header.ver2.encode('ascii').ljust(12, b'\x00')[:12]
        main_header[13:25] = ver2_bytes
        # Записываем ver3 (7 байт) по смещению 26
        ver3_bytes = header.ver3.encode('ascii').ljust(7, b'\x00')[:7]
        main_header[26:33] = ver3_bytes
        # Размер файла: 64 (заголовок) + длина сжатых (или нет) данных
        header.file_size = 64 + len(comp_data)
        main_header[34:38] = header.int_to_big_endian(header.file_size)
        # Записываем first_offset (64)
        main_header[38:42] = header.int_to_big_endian(header.first_offset)
        # Смещение 42-50 – оставляем нулевыми (как в C# вызывается reader.ReadBytes(8))
        main_header[45] = 1
        main_header[49] = 1
        # Записываем размер сжатых данных
        header.file_zip_size = len(comp_data)
        main_header[50:54] = header.int_to_big_endian(header.file_zip_size)
        # Записываем размер распакованных данных
        main_header[54:58] = header.int_to_big_endian(header.file_unzip_size)
        # Записываем last_offset = 64 + длина сжатых данных
        header.last_offset = 64 + len(comp_data)
        main_header[58:62] = header.int_to_big_endian(header.last_offset)
        # Остальные байты оставляем 0

        # Собираем окончательные данные: основной заголовок + (сжатые или нет) данные
        output_data = main_header + comp_data

        # Записываем результат в файл
        try:
            with open(output_filename, "wb") as outf:
                outf.write(output_data)
            QtWidgets.QMessageBox.information(self, "Success", "Packing completed successfully.")
        except Exception as e:
            QtWidgets.QMessageBox.critical(self, "Error", f"Failed to write output file:\n{str(e)}")

    def extract_files(self):
        """
        Распаковка файлов из unity3d.
        Читается 64-байтовый заголовок, затем сжатые данные, которые распаковываются.
        Из метаданных извлекается число файлов, имена, смещения и размеры,
        после чего файлы записываются в подпапку «uncompressfiles».
        """
        options = QtWidgets.QFileDialog.Options()
        input_filename, _ = QtWidgets.QFileDialog.getOpenFileName(
            self,
            "Select .unity3d file to extract",
            "",
            "Unity web player (*.unity3d);;All Files (*)",
            options=options
        )
        if not input_filename:
            return

        input_dir = os.path.dirname(input_filename)
        output_dir = os.path.join(input_dir, "uncompressfiles")
        os.makedirs(output_dir, exist_ok=True)

        try:
            with open(input_filename, "rb") as inf:
                # Читаем основной заголовок (64 байта)
                main_header = inf.read(64)
                if len(main_header) < 64:
                    raise Exception("File too short, invalid header.")

                # Разбираем поля заголовка
                flag = main_header[0:8].rstrip(b'\x00').decode('ascii')
                ver1 = main_header[12]
                ver2 = main_header[13:25].rstrip(b'\x00').decode('ascii')
                ver3 = main_header[26:33].rstrip(b'\x00').decode('ascii')
                file_size = int.from_bytes(main_header[34:38], 'big')
                first_offset = int.from_bytes(main_header[38:42], 'big')
                file_zip_size = int.from_bytes(main_header[50:54], 'big')
                file_unzip_size = int.from_bytes(main_header[54:58], 'big')
                last_offset = int.from_bytes(main_header[58:62], 'big')

                # Читаем оставшиеся данные – сжатый блок
                comp_data = inf.read()

            # Распаковываем данные
            decom_data = decompress_data(comp_data)

            # Первые 4 байта – число файлов
            num_files = int.from_bytes(decom_data[0:4], 'big')
            pos = 4
            for i in range(num_files):
                # Читаем имя файла (ASCII, оканчивается 0)
                name_bytes = bytearray()
                while decom_data[pos] != 0:
                    name_bytes.append(decom_data[pos])
                    pos += 1
                filename = name_bytes.decode('ascii')
                pos += 1  # пропускаем нулевой байт

                # Читаем смещение и размер файла (по 4 байта каждое)
                offset = int.from_bytes(decom_data[pos:pos+4], 'big')
                pos += 4
                size = int.from_bytes(decom_data[pos:pos+4], 'big')
                pos += 4

                # Извлекаем данные файла и записываем в выходной каталог
                file_data = decom_data[offset:offset+size]
                out_path = os.path.join(output_dir, filename)
                with open(out_path, "wb") as outf:
                    outf.write(file_data)
            QtWidgets.QMessageBox.information(self, "Success", "Files extracted successfully.")
        except Exception as e:
            QtWidgets.QMessageBox.critical(self, "Error", f"Extraction failed:\n{str(e)}")

    def extract_raw_header(self):
        """
        Распаковка сжатых данных (headerzip) без разбиения на файлы.
        Результат записывается в файл «uncompress_file» в той же папке.
        """
        options = QtWidgets.QFileDialog.Options()
        input_filename, _ = QtWidgets.QFileDialog.getOpenFileName(
            self,
            "Select .unity3d file to extract raw header",
            "",
            "Unity web player (*.unity3d);;All Files (*)",
            options=options
        )
        if not input_filename:
            return

        input_dir = os.path.dirname(input_filename)
        output_file = os.path.join(input_dir, "uncompress_file")

        try:
            with open(input_filename, "rb") as inf:
                main_header = inf.read(64)
                if len(main_header) < 64:
                    raise Exception("Invalid header.")
                comp_data = inf.read()
            decom_data = decompress_data(comp_data)
            with open(output_file, "wb") as outf:
                outf.write(decom_data)
            QtWidgets.QMessageBox.information(self, "Success", "Raw header extracted successfully.")
        except Exception as e:
            QtWidgets.QMessageBox.critical(self, "Error", f"Extraction failed:\n{str(e)}")

def main():
    app = QtWidgets.QApplication(sys.argv)
    window = MainWindow()
    window.show()
    sys.exit(app.exec_())

if __name__ == "__main__":
    main()

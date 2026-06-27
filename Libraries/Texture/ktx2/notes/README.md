# KTX-2 Codec

Better self-contained layout for your exporter

For your toolkit, I would bundle them together like this:

com.babylontoolkit.editor/Libraries/Texture/ktx2/osx64/ktx
com.babylontoolkit.editor/Libraries/Texture/ktx2/osx64/libktx.5.dylib

Then patch ktx so it looks for libktx.5.dylib next to itself:

install_name_tool -change @rpath/libktx.5.dylib @executable_path/libktx.5.dylib com.babylontoolkit.editor/Libraries/Texture/ktx2/osx64/ktx

Verify:

otool -L com.babylontoolkit.editor/Libraries/Texture/ktx2/osx64/ktx

You want to see:

@executable_path/libktx.5.dylib

Then test:

chmod +x com.babylontoolkit.editor/Libraries/Texture/ktx2/osx64/ktx
com.babylontoolkit.editor/Libraries/Texture/ktx2/osx64/ktx --version
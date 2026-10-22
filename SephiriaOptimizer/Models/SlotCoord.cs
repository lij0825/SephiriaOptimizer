namespace SephiriaOptimizer.Models {
	public readonly struct SlotCoord {
		public sbyte X { get; }
		public sbyte Y { get; }
		public int LinearIndex { get; }

		public SlotCoord(sbyte xCoord, sbyte yCoord, int linearIndex) {
			X = xCoord;
			Y = yCoord;
			LinearIndex = linearIndex;
		}

		public override string ToString() {
			return $"[{X}, {Y}]";
		}
	}
}

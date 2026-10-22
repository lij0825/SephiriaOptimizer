namespace SephiriaOptimizer.Models {
	public enum SlotItemKind : byte {
		Empty = 0,
		Artifact = 1,
		StoneTablet = 2,
		OtherItem = 3
	}

	public enum ArtifactPriorityTier : byte {
		Sacrifice = 0,
		Low = 1,
		Normal = 2,
		High = 3,
		Core = 4
	}

	public enum ArtifactConstraintType : byte {
		None = 0,
		TopInInventory = 1,
		BottomInInventory = 2,
		SideEnd = 3,
		Outlined = 4,
		Inside = 5,
		BothSideCharm = 6,
		BothSidesAreEmpty = 7,
		Near8MagicBook = 8,
		NeighborsAreFull = 9,
		FullHp = 10
	}

	public enum TabletConditionType : byte {
		None = 0,
		AnyItem = 1,
		OnlyCharm = 2,
		Placed = 3
	}

	public enum TabletEffectType : byte {
		None = 0,
		IncreaseConstLevel = 1,
		Disable = 2,
		IgnoreCriteria = 3,
		MultiplyConstLevel = 4
	}
}

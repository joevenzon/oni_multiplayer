﻿using System;
using MultiplayerMod.Game.Extension;
using UnityEngine;

namespace MultiplayerMod.Multiplayer.Objects.Reference;

[Serializable]
public class GridReference : GameObjectReference {

    public int Cell { get; }
    public int Layer { get; }

    public GridReference(int cell, int layer) {
        Cell = cell;
        Layer = layer;
    }

    public GridReference(GameObject gameObject) {
        var extension = gameObject.GetComponent<GameObjectExtension>();
        Cell = Grid.PosToCell(gameObject);
        Layer = extension != null ? extension.GridLayer : 0;
    }

    public override GameObject Resolve() => Grid.Objects[Cell, Layer];

    public override string ToString() => $"{{ Cell = {Cell}, Layer = {Layer} }}";

    protected bool Equals(GridReference other) => Cell == other.Cell && Layer == other.Layer;

    public override bool Equals(object? obj) {
        if (ReferenceEquals(null, obj))
            return false;
        if (ReferenceEquals(this, obj))
            return true;
        return obj.GetType() == GetType() && Equals((GridReference) obj);
    }

    public override int GetHashCode() => Cell * 397 ^ Layer;

}
